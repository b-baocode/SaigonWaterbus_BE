using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Application.Auth.Password;

public sealed record ForgotPasswordRequestOtpCommand(string Email) : IRequest<OtpChallengeDto>;

public sealed class ForgotPasswordRequestOtpCommandValidator : AbstractValidator<ForgotPasswordRequestOtpCommand>
{
    public ForgotPasswordRequestOtpCommandValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty()
            .MaximumLength(255)
            .EmailAddress();
    }
}

public sealed class ForgotPasswordRequestOtpCommandHandler : IRequestHandler<ForgotPasswordRequestOtpCommand, OtpChallengeDto>
{
    private readonly IApplicationDbContext _context;
    private readonly IIdentityNormalizer _identityNormalizer;
    private readonly ISecretHasher _secretHasher;
    private readonly IOtpCodeService _otpCodeService;
    private readonly IOtpSender _otpSender;
    private readonly IOtpPolicy _otpPolicy;
    private readonly TimeProvider _timeProvider;

    public ForgotPasswordRequestOtpCommandHandler(
        IApplicationDbContext context,
        IIdentityNormalizer identityNormalizer,
        ISecretHasher secretHasher,
        IOtpCodeService otpCodeService,
        IOtpSender otpSender,
        IOtpPolicy otpPolicy,
        TimeProvider timeProvider)
    {
        _context = context;
        _identityNormalizer = identityNormalizer;
        _secretHasher = secretHasher;
        _otpCodeService = otpCodeService;
        _otpSender = otpSender;
        _otpPolicy = otpPolicy;
        _timeProvider = timeProvider;
    }

    public async Task<OtpChallengeDto> Handle(ForgotPasswordRequestOtpCommand request, CancellationToken cancellationToken)
    {
        var normalizedEmail = _identityNormalizer.NormalizeEmail(request.Email);
        var challengeResult = await _context.ExecuteInTransactionAsync(async ct =>
        {
            var user = await _context.Set<User>()
                .SingleOrDefaultAsync(x => x.NormalizedEmail == normalizedEmail, ct);

            if (user is null)
            {
                throw AuthSupport.CreateValidationException(nameof(request.Email), "Email is not registered.");
            }

            AuthSupport.EnsureUserCanLogin(user, nameof(request.Email));

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
                throw AuthSupport.CreateValidationException(nameof(request.Email), "OTP was sent recently. Please wait before requesting again.");
            }

            await AuthSupport.RetirePendingOtpChallengesAsync(_context, user.Id, OtpPurpose.ForgotPassword, now, ct);

            var otpCode = _otpCodeService.GenerateCode();
            var challenge = new OtpChallenge
            {
                UserId = user.Id,
                Purpose = OtpPurpose.ForgotPassword,
                Email = user.Email,
                CodeHash = _secretHasher.Hash(otpCode),
                ExpiresAt = now.AddMinutes(_otpPolicy.ExpirationMinutes),
                ResendAvailableAt = now.AddSeconds(_otpPolicy.ResendSeconds),
                MaxAttempts = _otpPolicy.MaxAttempts
            };

            _context.Set<OtpChallenge>().Add(challenge);
            await _context.SaveChangesAsync(ct);

            return (
                Id: challenge.Id,
                Email: user.Email,
                FullName: user.FullName,
                Code: otpCode,
                ExpiresAt: challenge.ExpiresAt,
                ResendAvailableAt: challenge.ResendAvailableAt);
        }, cancellationToken);

        await _otpSender.SendAsync(
            challengeResult.Email,
            challengeResult.Code,
            OtpPurpose.ForgotPassword,
            challengeResult.FullName,
            cancellationToken);

        return new OtpChallengeDto(
            challengeResult.Id,
            _otpCodeService.MaskEmail(challengeResult.Email),
            challengeResult.ExpiresAt,
            challengeResult.ResendAvailableAt);
    }
}
