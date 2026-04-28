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
    private readonly ISmsOtpSender _smsOtpSender;
    private readonly IOtpPolicy _otpPolicy;
    private readonly IUserContext _userContext;
    private readonly TimeProvider _timeProvider;

    public ResendOtpCommandHandler(
        IApplicationDbContext context,
        ISecretHasher secretHasher,
        IOtpCodeService otpCodeService,
        IOtpSender otpSender,
        ISmsOtpSender smsOtpSender,
        IOtpPolicy otpPolicy,
        IUserContext userContext,
        TimeProvider timeProvider)
    {
        _context = context;
        _secretHasher = secretHasher;
        _otpCodeService = otpCodeService;
        _otpSender = otpSender;
        _smsOtpSender = smsOtpSender;
        _otpPolicy = otpPolicy;
        _userContext = userContext;
        _timeProvider = timeProvider;
    }

    public async Task<OtpChallengeDto> Handle(ResendOtpCommand request, CancellationToken cancellationToken)
    {
        var challenge = await _context.Set<OtpChallenge>()
            .Include(x => x.User)
            .SingleOrDefaultAsync(x => x.Id == request.ChallengeId, cancellationToken)
            ?? throw AuthSupport.CreateValidationException(nameof(request.ChallengeId), "OTP challenge was not found.");

        var purpose = challenge.Purpose;
        if (purpose is not (OtpPurpose.Register or OtpPurpose.ForgotPassword or OtpPurpose.EmailChange))
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
        else if (purpose == OtpPurpose.EmailChange)
        {
            if (_userContext.UserId != user.Id)
            {
                throw new UnauthorizedAccessException();
            }

            AuthSupport.EnsureUserCanLogin(user);
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
        var otpChannel = AuthSupport.ResolveOtpChannelFromDestination(latestChallenge.Email);

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
                Email = latestChallenge.Email,
                CodeHash = _secretHasher.Hash(otpCode),
                ExpiresAt = now.AddMinutes(_otpPolicy.ExpirationMinutes),
                ResendAvailableAt = now.AddSeconds(_otpPolicy.ResendSeconds),
                MaxAttempts = _otpPolicy.MaxAttempts
            };

            _context.Set<OtpChallenge>().Add(newChallenge);
            await _context.SaveChangesAsync(ct);

            return (
                Id: newChallenge.Id,
                Destination: newChallenge.Email,
                FullName: user.FullName,
                Code: otpCode,
                ExpiresAt: newChallenge.ExpiresAt,
                ResendAvailableAt: newChallenge.ResendAvailableAt);
        }, cancellationToken);

        if (otpChannel == OtpChannel.Email)
        {
            await _otpSender.SendAsync(
                resendResult.Destination,
                resendResult.Code,
                purpose,
                resendResult.FullName,
                cancellationToken);
        }
        else
        {
            await _smsOtpSender.SendAsync(
                resendResult.Destination,
                resendResult.Code,
                purpose,
                resendResult.FullName,
                cancellationToken);
        }

        return new OtpChallengeDto(
            resendResult.Id,
            otpChannel == OtpChannel.Email
                ? _otpCodeService.MaskEmail(resendResult.Destination)
                : _otpCodeService.MaskPhone(resendResult.Destination),
            resendResult.ExpiresAt,
            resendResult.ResendAvailableAt);
    }
}
