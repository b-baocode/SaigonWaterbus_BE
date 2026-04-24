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
        await using var transaction = await _context.BeginTransactionAsync(cancellationToken);

        var normalizedEmail = _identityNormalizer.NormalizeEmail(request.Email);
        var user = await _context.Set<User>()
            .SingleOrDefaultAsync(x => x.NormalizedEmail == normalizedEmail, cancellationToken);

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
            .FirstOrDefaultAsync(cancellationToken);

        if (existingActiveChallenge is not null && existingActiveChallenge.ResendAvailableAt > now)
        {
            throw AuthSupport.CreateValidationException(nameof(request.Email), "OTP was sent recently. Please wait before requesting again.");
        }

        await AuthSupport.RetirePendingOtpChallengesAsync(_context, user.Id, OtpPurpose.ForgotPassword, now, cancellationToken);

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
        await _context.SaveChangesAsync(cancellationToken);
        await _otpSender.SendAsync(user.Email, otpCode, OtpPurpose.ForgotPassword, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new OtpChallengeDto(
            challenge.Id,
            _otpCodeService.MaskEmail(user.Email),
            challenge.ExpiresAt,
            challenge.ResendAvailableAt);
    }
}
