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
        RuleFor(x => x.ChallengeId).GreaterThan(0);
        RuleFor(x => x.Code).NotEmpty().Length(4, 10);
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
            ?? throw AuthSupport.CreateValidationException(nameof(request.ChallengeId), "OTP challenge was not found.");

        var user = challenge.User;
        AuthSupport.EnsureUserCanLogin(user);

        var now = _timeProvider.GetUtcNow();

        if (challenge.ConsumedAt.HasValue)
        {
            throw AuthSupport.CreateValidationException(nameof(request.Code), "OTP has already been used.");
        }

        if (challenge.ExpiresAt <= now)
        {
            challenge.ConsumedAt = now;
            await _context.SaveChangesAsync(cancellationToken);
            throw AuthSupport.CreateValidationException(nameof(request.Code), "OTP has expired. Please request email verification again.");
        }

        if (!_secretHasher.Verify(request.Code, challenge.CodeHash))
        {
            challenge.AttemptCount += 1;

            if (challenge.AttemptCount >= challenge.MaxAttempts)
            {
                challenge.ConsumedAt = now;
            }

            await _context.SaveChangesAsync(cancellationToken);
            throw AuthSupport.CreateValidationException(nameof(request.Code), "OTP is invalid.");
        }

        return await _context.ExecuteInTransactionAsync(async ct =>
        {
            var normalizedEmail = _identityNormalizer.NormalizeEmail(challenge.Email);
            if (await _context.Set<User>().AnyAsync(x => x.NormalizedEmail == normalizedEmail && x.Id != user.Id, ct))
            {
                challenge.ConsumedAt = now;
                await _context.SaveChangesAsync(ct);
                throw AuthSupport.CreateValidationException(nameof(request.ChallengeId), "Email is already registered.");
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
