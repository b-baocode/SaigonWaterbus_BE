using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Application.Auth.Profile;

public sealed record VerifyPhoneChangeOtpCommand(int ChallengeId, string Code) : IRequest<AuthUserDto>;

public sealed class VerifyPhoneChangeOtpCommandValidator : AbstractValidator<VerifyPhoneChangeOtpCommand>
{
    public VerifyPhoneChangeOtpCommandValidator()
    {
        RuleFor(x => x.ChallengeId)
            .GreaterThan(0)
            .WithMessage("Mã xác thực không hợp lệ.");

        RuleFor(x => x.Code)
            .NotEmpty()
            .WithMessage("Mã OTP là bắt buộc.")
            .Length(4, 10)
            .WithMessage("Mã OTP không hợp lệ.");
    }
}

public sealed class VerifyPhoneChangeOtpCommandHandler : IRequestHandler<VerifyPhoneChangeOtpCommand, AuthUserDto>
{
    private readonly IApplicationDbContext _context;
    private readonly ISecretHasher _secretHasher;
    private readonly IUserContext _userContext;
    private readonly TimeProvider _timeProvider;

    public VerifyPhoneChangeOtpCommandHandler(
        IApplicationDbContext context,
        ISecretHasher secretHasher,
        IUserContext userContext,
        TimeProvider timeProvider)
    {
        _context = context;
        _secretHasher = secretHasher;
        _userContext = userContext;
        _timeProvider = timeProvider;
    }

    public async Task<AuthUserDto> Handle(VerifyPhoneChangeOtpCommand request, CancellationToken cancellationToken)
    {
        if (!_userContext.UserId.HasValue)
        {
            throw new UnauthorizedAccessException();
        }

        var challenge = await _context.Set<OtpChallenge>()
            .Include(x => x.User)
            .ThenInclude(x => x.Role)
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
            await _context.SaveChangesAsync(cancellationToken);
            throw AuthSupport.CreateValidationException(nameof(request.Code), "OTP đã hết hạn, vui lòng yêu cầu xác thực số điện thoại lại.");
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
            if (!await _context.Set<ExternalLogin>().AnyAsync(
                    x => x.UserId == user.Id && x.Provider == AuthSupport.GoogleProvider,
                    ct))
            {
                challenge.ConsumedAt = now;
                await _context.SaveChangesAsync(ct);
                throw AuthSupport.CreateValidationException(nameof(request.ChallengeId), "Chỉ tài khoản đăng nhập Google mới được tự cập nhật số điện thoại.");
            }

            if (user.NormalizedPhoneNumber is not null)
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

            if (await _context.Set<User>().AnyAsync(
                    x => x.NormalizedPhoneNumber == challenge.PendingPhoneNumber && x.Id != user.Id,
                    ct))
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

            user.PhoneNumber = PhoneRules.ToInternationalFormat(challenge.PendingPhoneNumber);
            user.NormalizedPhoneNumber = challenge.PendingPhoneNumber;
            user.PhoneVerifiedAt = now;

            await _context.SaveChangesAsync(ct);
            return AuthSupport.CreateUserDto(user);
        }, cancellationToken);
    }
}
