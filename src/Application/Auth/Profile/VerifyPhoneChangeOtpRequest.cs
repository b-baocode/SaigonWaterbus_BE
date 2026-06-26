using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Application.Auth.Profile;

public sealed record VerifyPhoneChangeOtpRequest(Guid ChallengeId, string Code);

public sealed class VerifyPhoneChangeOtpRequestValidator : AbstractValidator<VerifyPhoneChangeOtpRequest>
{
    public VerifyPhoneChangeOtpRequestValidator()
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

public sealed class VerifyPhoneChangeOtpRequestUseCase
{
    private readonly IApplicationDbContext _context;
    private readonly ISecretHasher _secretHasher;
    private readonly IUserContext _userContext;
    private readonly IOtpCache _otpCache;
    private readonly TimeProvider _timeProvider;

    public VerifyPhoneChangeOtpRequestUseCase(
        IApplicationDbContext context,
        ISecretHasher secretHasher,
        IUserContext userContext,
        TimeProvider timeProvider,
        IOtpCache? otpCache = null)
    {
        _context = context;
        _secretHasher = secretHasher;
        _userContext = userContext;
        _otpCache = otpCache ?? NullOtpCache.Instance;
        _timeProvider = timeProvider;
    }

    public async Task<AuthUserDto> ExecuteAsync(VerifyPhoneChangeOtpRequest request, CancellationToken cancellationToken)
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
                  && x.Purpose == OtpPurpose.PhoneChange
                  && x.UserId == _userContext.UserId.Value,
                cancellationToken)
            ?? throw AuthSupport.CreateValidationException(nameof(request.ChallengeId), "Không tìm thấy yêu cầu xác thực OTP.");

        var user = challenge.User;
        AuthSupport.EnsureUserCanLogin(user, requireVerifiedPhone: false);

        var now = _timeProvider.GetUtcNow();
        challenge = await AuthSupport.ResolveLatestPendingOtpChallengeAsync(
            _context,
            challenge,
            OtpPurpose.PhoneChange,
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
            throw AuthSupport.CreateValidationException(nameof(request.Code), "OTP đã hết hạn, vui lòng yêu cầu xác thực số điện thoại lại.");
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
            if (user.PhoneNumber is not null)
            {
                challenge.ConsumedAt = now;
                await _context.SaveChangesAsync(ct);
                throw AuthSupport.CreateValidationException(nameof(request.ChallengeId), "Số điện thoại chỉ được cập nhật một lần.");
            }

            if (string.IsNullOrWhiteSpace(challenge.PendingPhoneNumber)
                || !PhoneRules.IsValid(challenge.PendingPhoneNumber))
            {
                challenge.ConsumedAt = now;
                await _context.SaveChangesAsync(ct);
                throw AuthSupport.CreateValidationException(nameof(request.ChallengeId), "Số điện thoại chờ xác thực không hợp lệ.");
            }

            if (await AuthSupport.WhereUserIdentityMatches(_context.Set<User>(), challenge.PendingPhoneNumber, null)
                    .AnyAsync(x => x.Id != user.Id, ct))
            {
                challenge.ConsumedAt = now;
                await _context.SaveChangesAsync(ct);
                throw AuthSupport.CreateValidationException(nameof(request.ChallengeId), "Số điện thoại đã được đăng ký.");
            }

            var otherPendingChallenges = await _context.Set<OtpChallenge>()
                .Where(x => x.UserId == user.Id
                         && x.Purpose == OtpPurpose.PhoneChange
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

            user.PhoneNumber = PhoneRules.ToInternationalFormat(challenge.PendingPhoneNumber);
            user.NormalizedPhoneNumber = challenge.PendingPhoneNumber;
            user.PhoneVerifiedAt = now;

            await _context.SaveChangesAsync(ct);
            return AuthSupport.CreateUserDto(user);
        }, cancellationToken);
    }
}
