using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Application.Auth.Profile;

public sealed record VerifyEmailChangeOtpCommand(int ChallengeId, string Code) : IRequest<AuthUserDto>;

public sealed class VerifyEmailChangeOtpCommandValidator : AbstractValidator<VerifyEmailChangeOtpCommand>
{
    public VerifyEmailChangeOtpCommandValidator()
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

public sealed class VerifyEmailChangeOtpCommandHandler : IRequestHandler<VerifyEmailChangeOtpCommand, AuthUserDto>
{
    private readonly IApplicationDbContext _context;
    private readonly IIdentityNormalizer _identityNormalizer;
    private readonly ISecretHasher _secretHasher;
    private readonly IUserContext _userContext;
    private readonly TimeProvider _timeProvider;

    public VerifyEmailChangeOtpCommandHandler(
        IApplicationDbContext context,
        IIdentityNormalizer identityNormalizer,
        ISecretHasher secretHasher,
        IUserContext userContext,
        TimeProvider timeProvider)
    {
        _context = context;
        _identityNormalizer = identityNormalizer;
        _secretHasher = secretHasher;
        _userContext = userContext;
        _timeProvider = timeProvider;
    }

    public async Task<AuthUserDto> Handle(VerifyEmailChangeOtpCommand request, CancellationToken cancellationToken)
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
            await _context.SaveChangesAsync(cancellationToken);
            throw AuthSupport.CreateValidationException(nameof(request.Code), "OTP đã hết hạn, vui lòng yêu cầu xác thực email lại.");
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
            var normalizedEmail = _identityNormalizer.NormalizeEmail(challenge.Email);
            if (await _context.Set<User>().AnyAsync(x => x.NormalizedEmail == normalizedEmail && x.Id != user.Id, ct))
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

            user.Email = challenge.Email.Trim();
            user.NormalizedEmail = normalizedEmail;

            await _context.SaveChangesAsync(ct);
            return AuthSupport.CreateUserDto(user);
        }, cancellationToken);
    }
}
