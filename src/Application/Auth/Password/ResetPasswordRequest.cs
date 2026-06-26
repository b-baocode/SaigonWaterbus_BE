using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Application.Auth.Password;

public sealed record ResetPasswordRequest(Guid ChallengeId, string Code, string NewPassword);

public sealed class ResetPasswordRequestValidator : AbstractValidator<ResetPasswordRequest>
{
    public ResetPasswordRequestValidator()
    {
        RuleFor(x => x.ChallengeId)
            .NotEmpty()
            .WithMessage("Mã xác thực không hợp lệ.");

        RuleFor(x => x.Code)
            .NotEmpty()
            .WithMessage("Mã OTP là bắt buộc.")
            .Length(4, 10)
            .WithMessage("Mã OTP không hợp lệ.");

        RuleFor(x => x.NewPassword)
            .NotEmpty()
            .WithMessage("Mật khẩu mới là bắt buộc.")
            .Must(PasswordRules.IsStrong)
            .WithMessage(PasswordRules.StrongPasswordMessage);
    }
}

public sealed class ResetPasswordRequestUseCase
{
    private readonly IApplicationDbContext _context;
    private readonly ISecretHasher _secretHasher;
    private readonly IOtpCache _otpCache;
    private readonly TimeProvider _timeProvider;

    public ResetPasswordRequestUseCase(
        IApplicationDbContext context,
        ISecretHasher secretHasher,
        TimeProvider timeProvider,
        IOtpCache? otpCache = null)
    {
        _context = context;
        _secretHasher = secretHasher;
        _otpCache = otpCache ?? NullOtpCache.Instance;
        _timeProvider = timeProvider;
    }

    public async Task<AuthActionResultDto> ExecuteAsync(ResetPasswordRequest request, CancellationToken cancellationToken)
    {
        var challenge = await _context.Set<OtpChallenge>()
            .Include(x => x.User)
            .SingleOrDefaultAsync(
                x => x.Id == request.ChallengeId && x.Purpose == OtpPurpose.ForgotPassword,
                cancellationToken)
            ?? throw AuthSupport.CreateValidationException(nameof(request.ChallengeId), "Không tìm thấy yêu cầu xác thực OTP.");

        var now = _timeProvider.GetUtcNow();
        challenge = await AuthSupport.ResolveLatestPendingOtpChallengeAsync(
            _context,
            challenge,
            OtpPurpose.ForgotPassword,
            cancellationToken);

        if (challenge.ConsumedAt.HasValue)
        {
            throw AuthSupport.CreateValidationException(nameof(request.Code), "OTP đã được sử dụng.");
        }

        if (challenge.ExpiresAt <= now)
        {
            await _otpCache.RemoveAsync(challenge.Id, cancellationToken);
            throw AuthSupport.CreateValidationException(nameof(request.Code), "OTP đã hết hạn.");
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

        challenge.AttemptCount += 1;
        challenge.ConsumedAt = now;
        await _otpCache.RemoveAsync(challenge.Id, cancellationToken);

        var user = challenge.User;
        AuthSupport.EnsureUserCanLogin(user);
        user.PasswordHash = _secretHasher.Hash(request.NewPassword);

        await AuthSupport.RevokeActiveRefreshTokensAsync(_context, user.Id, now, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);

        return new AuthActionResultDto("Dat lai mat khau thanh cong.");
    }
}
