using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Application.Auth.Register;

public sealed record VerifyRegisterOtpRequest(Guid ChallengeId, string Code);

public sealed class VerifyRegisterOtpRequestValidator : AbstractValidator<VerifyRegisterOtpRequest>
{
    public VerifyRegisterOtpRequestValidator()
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

public sealed class VerifyRegisterOtpRequestUseCase
{
    private readonly IApplicationDbContext _context;
    private readonly ISecretHasher _secretHasher;
    private readonly IUserCodeGenerator _userCodeGenerator;
    private readonly TimeProvider _timeProvider;

    public VerifyRegisterOtpRequestUseCase(
        IApplicationDbContext context,
        ISecretHasher secretHasher,
        IUserCodeGenerator userCodeGenerator,
        TimeProvider timeProvider)
    {
        _context = context;
        _secretHasher = secretHasher;
        _userCodeGenerator = userCodeGenerator;
        _timeProvider = timeProvider;
    }

    public async Task<AuthActionResultDto> ExecuteAsync(VerifyRegisterOtpRequest request, CancellationToken cancellationToken)
    {
        var challenge = await _context.Set<OtpChallenge>()
            .Include(x => x.User)
            .SingleOrDefaultAsync(
                x => x.Id == request.ChallengeId && x.Purpose == OtpPurpose.Register,
                cancellationToken)
            ?? throw AuthSupport.CreateValidationException(nameof(request.ChallengeId), "Không tìm thấy yêu cầu xác thực OTP.");

        var now = _timeProvider.GetUtcNow();
        challenge = await AuthSupport.ResolveLatestPendingOtpChallengeAsync(
            _context,
            challenge,
            OtpPurpose.Register,
            cancellationToken);

        if (challenge.ConsumedAt.HasValue)
        {
            throw AuthSupport.CreateValidationException(nameof(request.Code), "OTP đã được sử dụng.");
        }

        if (challenge.ExpiresAt <= now)
        {
            if (await AuthSupport.RemovePendingRegistrationUserIfExpiredAsync(
                    _context,
                    challenge.UserId,
                    now,
                    cancellationToken))
            {
                await _context.SaveChangesAsync(cancellationToken);
            }

            throw AuthSupport.CreateValidationException(
                nameof(request.Code),
                "OTP đã hết hạn, đăng ký đã bị hủy. Vui lòng đăng ký lại.");
        }

        if (!_secretHasher.Verify(request.Code, challenge.CodeHash))
        {
            challenge.AttemptCount += 1;

            if (challenge.AttemptCount >= challenge.MaxAttempts)
            {
                challenge.ConsumedAt = now;
            }

            await _context.SaveChangesAsync(cancellationToken);
            throw AuthSupport.CreateValidationException(nameof(request.Code), "OTP không hợp lệ.");
        }

        return await _context.ExecuteInTransactionAsync(async ct =>
        {
            challenge.AttemptCount += 1;
            challenge.ConsumedAt = now;

            var user = challenge.User;
            user.Status = UserStatus.Active;
            if (AuthSupport.ResolveOtpChannelFromDestination(challenge.Email) == OtpChannel.Phone)
            {
                user.PhoneVerifiedAt ??= now;
            }

            var customerRole = await AuthSupport.GetRoleByCodeAsync(
                _context,
                Roles.CustomerCode,
                ct);
            user.RoleId = customerRole.Id;

            if (!UserCodes.HasPrefix(user.UserCode, UserCodes.CustomerPrefix))
            {
                user.UserCode = await _userCodeGenerator.GenerateNextCodeAsync(customerRole.Code, ct);
            }

            await _context.SaveChangesAsync(ct);
            return new AuthActionResultDto("Xac nhan OTP thanh cong.");
        }, cancellationToken);
    }
}
