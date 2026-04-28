using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Application.Auth.Password;

public sealed record ForgotPasswordRequestOtpCommand(string? Email = null, string? Phone = null) : IRequest<OtpChallengeDto>;

public sealed class ForgotPasswordRequestOtpCommandValidator : AbstractValidator<ForgotPasswordRequestOtpCommand>
{
    public ForgotPasswordRequestOtpCommandValidator()
    {
        RuleFor(x => x.Email)
            .MaximumLength(255)
            .EmailAddress()
            .When(x => !string.IsNullOrWhiteSpace(x.Email));

        RuleFor(x => x.Phone)
            .Must(PhoneRules.IsValid)
            .WithMessage("Phone number must contain exactly 10 digits.")
            .When(x => !string.IsNullOrWhiteSpace(x.Phone));

        RuleFor(x => x)
            .Must(x => !string.IsNullOrWhiteSpace(x.Email) || !string.IsNullOrWhiteSpace(x.Phone))
            .WithMessage("Email or Phone is required.");
    }
}

public sealed class ForgotPasswordRequestOtpCommandHandler : IRequestHandler<ForgotPasswordRequestOtpCommand, OtpChallengeDto>
{
    private readonly IApplicationDbContext _context;
    private readonly IIdentityNormalizer _identityNormalizer;
    private readonly ISecretHasher _secretHasher;
    private readonly IOtpCodeService _otpCodeService;
    private readonly ISmsOtpSender _smsOtpSender;
    private readonly IOtpPolicy _otpPolicy;
    private readonly TimeProvider _timeProvider;

    public ForgotPasswordRequestOtpCommandHandler(
        IApplicationDbContext context,
        IIdentityNormalizer identityNormalizer,
        ISecretHasher secretHasher,
        IOtpCodeService otpCodeService,
        ISmsOtpSender smsOtpSender,
        IOtpPolicy otpPolicy,
        TimeProvider timeProvider)
    {
        _context = context;
        _identityNormalizer = identityNormalizer;
        _secretHasher = secretHasher;
        _otpCodeService = otpCodeService;
        _smsOtpSender = smsOtpSender;
        _otpPolicy = otpPolicy;
        _timeProvider = timeProvider;
    }

    public async Task<OtpChallengeDto> Handle(ForgotPasswordRequestOtpCommand request, CancellationToken cancellationToken)
    {
        var hasPhone = !string.IsNullOrWhiteSpace(request.Phone);
        var normalizedPhone = hasPhone
            ? _identityNormalizer.NormalizePhone(request.Phone!)
            : null;
        var normalizedEmail = !string.IsNullOrWhiteSpace(request.Email)
            ? _identityNormalizer.NormalizeEmail(request.Email)
            : null;
        var lookupProperty = hasPhone ? nameof(request.Phone) : nameof(request.Email);

        var challengeResult = await _context.ExecuteInTransactionAsync(async ct =>
        {
            var usersQuery = _context.Set<User>().AsQueryable();
            if (normalizedPhone is not null && normalizedEmail is not null)
            {
                usersQuery = usersQuery.Where(x => x.NormalizedPhoneNumber == normalizedPhone && x.NormalizedEmail == normalizedEmail);
            }
            else if (normalizedPhone is not null)
            {
                usersQuery = usersQuery.Where(x => x.NormalizedPhoneNumber == normalizedPhone);
            }
            else
            {
                usersQuery = usersQuery.Where(x => x.NormalizedEmail == normalizedEmail);
            }

            var user = await usersQuery.SingleOrDefaultAsync(ct);

            if (user is null)
            {
                throw AuthSupport.CreateValidationException(lookupProperty, "Account is not registered.");
            }

            AuthSupport.EnsureUserCanLogin(user, lookupProperty);

            if (string.IsNullOrWhiteSpace(user.NormalizedPhoneNumber))
            {
                throw AuthSupport.CreateValidationException(nameof(request.Phone), "Phone number is not available for this account.");
            }

            var now = _timeProvider.GetUtcNow();
            var existingActiveChallenge = await _context.Set<OtpChallenge>()
                .Where(x => x.UserId == user.Id
                         && x.Purpose == OtpPurpose.ForgotPassword
                         && x.ConsumedAt == null
                         && x.ExpiresAt > now)
                .OrderByDescending(x => x.Id)
                .FirstOrDefaultAsync(ct);

            if (existingActiveChallenge is not null && existingActiveChallenge.ResendAvailableAt > now)
            {
                throw AuthSupport.CreateValidationException(lookupProperty, "OTP was sent recently. Please wait before requesting again.");
            }

            await AuthSupport.RetirePendingOtpChallengesAsync(_context, user.Id, OtpPurpose.ForgotPassword, now, ct);

            var otpCode = _otpCodeService.GenerateCode();
            var destinationPhone = user.NormalizedPhoneNumber;
            var challenge = new OtpChallenge
            {
                UserId = user.Id,
                Purpose = OtpPurpose.ForgotPassword,
                Email = destinationPhone,
                CodeHash = _secretHasher.Hash(otpCode),
                ExpiresAt = now.AddMinutes(_otpPolicy.ExpirationMinutes),
                ResendAvailableAt = now.AddSeconds(_otpPolicy.ResendSeconds),
                MaxAttempts = _otpPolicy.MaxAttempts
            };

            _context.Set<OtpChallenge>().Add(challenge);
            await _context.SaveChangesAsync(ct);

            return (
                Id: challenge.Id,
                Phone: destinationPhone,
                FullName: user.FullName,
                Code: otpCode,
                ExpiresAt: challenge.ExpiresAt,
                ResendAvailableAt: challenge.ResendAvailableAt);
        }, cancellationToken);

        await _smsOtpSender.SendAsync(
            challengeResult.Phone,
            challengeResult.Code,
            OtpPurpose.ForgotPassword,
            challengeResult.FullName,
            cancellationToken);

        return new OtpChallengeDto(
            challengeResult.Id,
            _otpCodeService.MaskPhone(challengeResult.Phone),
            challengeResult.ExpiresAt,
            challengeResult.ResendAvailableAt);
    }
}
