using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Application.Auth.Profile;

public sealed record VerifyEmailChangeOtpRequest(Guid ChallengeId, string Code);

public sealed class VerifyEmailChangeOtpRequestValidator : AbstractValidator<VerifyEmailChangeOtpRequest>
{
    public VerifyEmailChangeOtpRequestValidator()
    {
        RuleFor(x => x.ChallengeId)
            .NotEmpty()
            .WithMessage("Mã xác thực không hợp lệ.");

        RuleFor(x => x.Code)
            .NotEmpty()
            .WithMessage("Mã OTP là bắt buộc.")
            .Length(4, 10)
            .WithMessage("Mã OTP không hợp lệ.");
    }
}

public sealed class VerifyEmailChangeOtpRequestUseCase
{
    private readonly IApplicationDbContext _context;
    private readonly IIdentityNormalizer _identityNormalizer;
    private readonly ISecretHasher _secretHasher;
    private readonly IUserContext _userContext;
    private readonly IOtpCache _otpCache;
    private readonly TimeProvider _timeProvider;

    public VerifyEmailChangeOtpRequestUseCase(
        IApplicationDbContext context,
        IIdentityNormalizer identityNormalizer,
        ISecretHasher secretHasher,
        IUserContext userContext,
        TimeProvider timeProvider,
        IOtpCache? otpCache = null)
    {
        _context = context;
        _identityNormalizer = identityNormalizer;
        _secretHasher = secretHasher;
        _userContext = userContext;
        _otpCache = otpCache ?? NullOtpCache.Instance;
        _timeProvider = timeProvider;
    }

    public async Task<AuthUserDto> ExecuteAsync(VerifyEmailChangeOtpRequest request, CancellationToken cancellationToken)
    {
        if (!_userContext.UserId.HasValue)
        {
            throw new UnauthorizedAccessException();
        }

        var challenge = await _context.Set<OtpChallenge>()
            .Include(x => x.User).ThenInclude(x => x.Role)
            .Include(x => x.User).ThenInclude(u => u.StationAssignments).ThenInclude(a => a.Station)
            .SingleOrDefaultAsync(
                x => x.Id == request.ChallengeId
                  && x.Purpose == OtpPurpose.EmailChange
                  && x.UserId == _userContext.UserId.Value,
                cancellationToken)
            ?? throw AuthSupport.CreateValidationException(nameof(request.ChallengeId), "Không tìm thấy yêu cầu xác thực OTP.");

        var user = challenge.User;
        AuthSupport.EnsureUserCanLogin(user, requireVerifiedPhone: false);

        var now = _timeProvider.GetUtcNow();
        challenge = await AuthSupport.ResolveLatestPendingOtpChallengeAsync(
            _context,
            challenge,
            OtpPurpose.EmailChange,
            cancellationToken);

        if (challenge.ConsumedAt.HasValue)
        {
            throw AuthSupport.CreateValidationException(nameof(request.Code), "OTP đã được sử dụng.");
        }

        if (challenge.ExpiresAt <= now)
        {
            challenge.ConsumedAt = now;
            await _otpCache.RemoveAsync(challenge.Id, cancellationToken);
            await _context.SaveChangesAsync(cancellationToken);
            throw AuthSupport.CreateValidationException(nameof(request.Code), "OTP đã hết hạn, vui lòng yêu cầu xác thực email lại.");
        }

        var codeHash = await _otpCache.GetCodeHashAsync(challenge.Id, cancellationToken) ?? challenge.CodeHash;
        if (!_secretHasher.Verify(request.Code, codeHash))
        {
            challenge.AttemptCount += 1;

            if (challenge.AttemptCount >= challenge.MaxAttempts)
            {
                challenge.ConsumedAt = now;
                await _otpCache.RemoveAsync(challenge.Id, cancellationToken);
            }

            await _context.SaveChangesAsync(cancellationToken);
            throw AuthSupport.CreateValidationException(nameof(request.Code), "OTP không hợp lệ.");
        }

        return await _context.ExecuteInTransactionAsync(async ct =>
        {
            var normalizedEmail = _identityNormalizer.NormalizeEmail(challenge.Email);
            if (await AuthSupport.WhereUserIdentityMatches(_context.Set<User>(), null, normalizedEmail)
                    .AnyAsync(x => x.Id != user.Id, ct))
            {
                challenge.ConsumedAt = now;
                await _context.SaveChangesAsync(ct);
                throw AuthSupport.CreateValidationException(nameof(request.ChallengeId), "Email đã được đăng ký.");
            }

            var otherPendingChallenges = await _context.Set<OtpChallenge>()
                .Where(x => x.UserId == user.Id
                         && x.Purpose == OtpPurpose.EmailChange
                         && x.Id != challenge.Id
                         && x.ConsumedAt == null)
                .ToListAsync(ct);

            foreach (var pendingChallenge in otherPendingChallenges)
            {
                pendingChallenge.ConsumedAt = now;
            }

            challenge.AttemptCount += 1;
            challenge.ConsumedAt = now;
            await _otpCache.RemoveAsync(challenge.Id, ct);

            user.Email = challenge.Email.Trim();
            user.NormalizedEmail = normalizedEmail;

            await _context.SaveChangesAsync(ct);
            return AuthSupport.CreateUserDto(user);
        }, cancellationToken);
    }
}
