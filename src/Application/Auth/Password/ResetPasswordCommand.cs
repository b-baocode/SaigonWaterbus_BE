using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Application.Auth.Password;

public sealed record ResetPasswordCommand(int ChallengeId, string Code, string NewPassword) : IRequest<AuthActionResultDto>;

public sealed class ResetPasswordCommandValidator : AbstractValidator<ResetPasswordCommand>
{
    public ResetPasswordCommandValidator()
    {
        RuleFor(x => x.ChallengeId).GreaterThan(0);
        RuleFor(x => x.Code).NotEmpty().Length(4, 10);
        RuleFor(x => x.NewPassword).NotEmpty().MinimumLength(8);
    }
}

public sealed class ResetPasswordCommandHandler : IRequestHandler<ResetPasswordCommand, AuthActionResultDto>
{
    private readonly IApplicationDbContext _context;
    private readonly ISecretHasher _secretHasher;
    private readonly TimeProvider _timeProvider;

    public ResetPasswordCommandHandler(
        IApplicationDbContext context,
        ISecretHasher secretHasher,
        TimeProvider timeProvider)
    {
        _context = context;
        _secretHasher = secretHasher;
        _timeProvider = timeProvider;
    }

    public async Task<AuthActionResultDto> Handle(ResetPasswordCommand request, CancellationToken cancellationToken)
    {
        var challenge = await _context.Set<OtpChallenge>()
            .Include(x => x.User)
            .SingleOrDefaultAsync(
                x => x.Id == request.ChallengeId && x.Purpose == OtpPurpose.ForgotPassword,
                cancellationToken)
            ?? throw AuthSupport.CreateValidationException(nameof(request.ChallengeId), "OTP challenge was not found.");

        var now = _timeProvider.GetUtcNow();

        if (challenge.ConsumedAt.HasValue)
        {
            throw AuthSupport.CreateValidationException(nameof(request.Code), "OTP has already been used.");
        }

        if (challenge.ExpiresAt <= now)
        {
            throw AuthSupport.CreateValidationException(nameof(request.Code), "OTP has expired.");
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

        challenge.AttemptCount += 1;
        challenge.ConsumedAt = now;

        var user = challenge.User;
        AuthSupport.EnsureUserCanLogin(user);
        user.PasswordHash = _secretHasher.Hash(request.NewPassword);

        await AuthSupport.RevokeActiveRefreshTokensAsync(_context, user.Id, now, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);

        return new AuthActionResultDto("Dat lai mat khau thanh cong.");
    }
}
