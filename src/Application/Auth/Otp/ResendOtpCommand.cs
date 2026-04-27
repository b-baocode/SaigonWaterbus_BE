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

        if (purpose == OtpPurpose.Register && latestChallenge.ExpiresAt <= now)
        {
            if (await AuthSupport.RemovePendingRegistrationUserIfExpiredAsync(
                    _context,
                    user.Id,
                    now,
                    cancellationToken))
            {
                await _context.SaveChangesAsync(cancellationToken);
            }

            throw AuthSupport.CreateValidationException(
                nameof(request.ChallengeId),
                "OTP has expired. Registration has been cancelled. Please register again.");
        }

        if (latestChallenge.ConsumedAt == null && latestChallenge.ResendAvailableAt > now)
        {
            throw AuthSupport.CreateValidationException(nameof(request.ChallengeId), "OTP was sent recently. Please wait before requesting again.");
        }

        var resendResult = await _context.ExecuteInTransactionAsync(async ct =>
        {
            await AuthSupport.RetirePendingOtpChallengesAsync(_context, user.Id, purpose, now, ct);

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
            await _context.SaveChangesAsync(ct);

            return (
                Id: newChallenge.Id,
                Email: user.Email,
                FullName: user.FullName,
                Code: otpCode,
                ExpiresAt: newChallenge.ExpiresAt,
                ResendAvailableAt: newChallenge.ResendAvailableAt);
        }, cancellationToken);

        await _otpSender.SendAsync(
            resendResult.Email,
            resendResult.Code,
            purpose,
            resendResult.FullName,
            cancellationToken);

        return new OtpChallengeDto(
            resendResult.Id,
            _otpCodeService.MaskEmail(resendResult.Email),
            resendResult.ExpiresAt,
            resendResult.ResendAvailableAt);
    }
}
