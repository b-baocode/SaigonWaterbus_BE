using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Application.Auth.Otp;

public sealed record ResendOtpCommand(int ChallengeId) : IRequest<OtpChallengeDto>;

public sealed class ResendOtpCommandValidator : AbstractValidator<ResendOtpCommand>
{
    public ResendOtpCommandValidator()
    {
        RuleFor(x => x.ChallengeId).GreaterThan(0);
    }
}

public sealed class ResendOtpCommandHandler : IRequestHandler<ResendOtpCommand, OtpChallengeDto>
{
    private readonly IApplicationDbContext _context;
    private readonly ISecretHasher _secretHasher;
    private readonly IOtpCodeService _otpCodeService;
    private readonly IOtpSender _otpSender;
    private readonly IOtpPolicy _otpPolicy;
    private readonly TimeProvider _timeProvider;

    public ResendOtpCommandHandler(
        IApplicationDbContext context,
        ISecretHasher secretHasher,
        IOtpCodeService otpCodeService,
        IOtpSender otpSender,
        IOtpPolicy otpPolicy,
        TimeProvider timeProvider)
    {
        _context = context;
        _secretHasher = secretHasher;
        _otpCodeService = otpCodeService;
        _otpSender = otpSender;
        _otpPolicy = otpPolicy;
        _timeProvider = timeProvider;
    }

    public async Task<OtpChallengeDto> Handle(ResendOtpCommand request, CancellationToken cancellationToken)
    {
        var challenge = await _context.Set<OtpChallenge>()
            .Include(x => x.User)
            .SingleOrDefaultAsync(x => x.Id == request.ChallengeId, cancellationToken)
            ?? throw AuthSupport.CreateValidationException(nameof(request.ChallengeId), "OTP challenge was not found.");

        var purpose = challenge.Purpose;
        if (purpose is not (OtpPurpose.Register or OtpPurpose.ForgotPassword))
        {
            throw AuthSupport.CreateValidationException(nameof(request.ChallengeId), "OTP challenge does not support resend.");
        }

        var user = challenge.User;
        if (purpose == OtpPurpose.Register)
        {
            if (user.Status != UserStatus.PendingVerification)
            {
                throw AuthSupport.CreateValidationException(nameof(request.ChallengeId), "Account has already completed OTP verification.");
            }
        }
        else
        {
            AuthSupport.EnsureUserCanLogin(user);
        }

        var now = _timeProvider.GetUtcNow();
        var latestChallenge = await _context.Set<OtpChallenge>()
            .Where(x => x.UserId == user.Id && x.Purpose == purpose)
            .OrderByDescending(x => x.Id)
            .FirstAsync(cancellationToken);

        if (latestChallenge.ConsumedAt == null && latestChallenge.ResendAvailableAt > now)
        {
            throw AuthSupport.CreateValidationException(nameof(request.ChallengeId), "OTP was sent recently. Please wait before requesting again.");
        }

        await using var transaction = await _context.BeginTransactionAsync(cancellationToken);

        await AuthSupport.RetirePendingOtpChallengesAsync(_context, user.Id, purpose, now, cancellationToken);

        var otpCode = _otpCodeService.GenerateCode();
        var newChallenge = new OtpChallenge
        {
            UserId = user.Id,
            Purpose = purpose,
            Email = user.Email,
            CodeHash = _secretHasher.Hash(otpCode),
            ExpiresAt = now.AddMinutes(_otpPolicy.ExpirationMinutes),
            ResendAvailableAt = now.AddSeconds(_otpPolicy.ResendSeconds),
            MaxAttempts = _otpPolicy.MaxAttempts
        };

        _context.Set<OtpChallenge>().Add(newChallenge);
        await _context.SaveChangesAsync(cancellationToken);
        await _otpSender.SendAsync(user.Email, otpCode, purpose, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new OtpChallengeDto(
            newChallenge.Id,
            _otpCodeService.MaskEmail(user.Email),
            newChallenge.ExpiresAt,
            newChallenge.ResendAvailableAt);
    }
}
