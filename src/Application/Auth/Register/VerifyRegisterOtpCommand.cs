using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Application.Auth.Register;

public sealed record VerifyRegisterOtpCommand(int ChallengeId, string Code) : IRequest<AuthActionResultDto>;

public sealed class VerifyRegisterOtpCommandValidator : AbstractValidator<VerifyRegisterOtpCommand>
{
    public VerifyRegisterOtpCommandValidator()
    {
        RuleFor(x => x.ChallengeId).GreaterThan(0);
        RuleFor(x => x.Code).NotEmpty().Length(4, 10);
    }
}

public sealed class VerifyRegisterOtpCommandHandler : IRequestHandler<VerifyRegisterOtpCommand, AuthActionResultDto>
{
    private readonly IApplicationDbContext _context;
    private readonly ISecretHasher _secretHasher;
    private readonly IUserCodeGenerator _userCodeGenerator;
    private readonly TimeProvider _timeProvider;

    public VerifyRegisterOtpCommandHandler(
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

    public async Task<AuthActionResultDto> Handle(VerifyRegisterOtpCommand request, CancellationToken cancellationToken)
    {
        var challenge = await _context.Set<OtpChallenge>()
            .Include(x => x.User)
            .SingleOrDefaultAsync(
                x => x.Id == request.ChallengeId && x.Purpose == OtpPurpose.Register,
                cancellationToken)
            ?? throw AuthSupport.CreateValidationException(nameof(request.ChallengeId), "OTP challenge was not found.");

        var now = _timeProvider.GetUtcNow();

        if (challenge.ConsumedAt.HasValue)
        {
            throw AuthSupport.CreateValidationException(nameof(request.Code), "OTP has already been used.");
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
                "OTP has expired. Registration has been cancelled. Please register again.");
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
            challenge.AttemptCount += 1;
            challenge.ConsumedAt = now;

            var user = challenge.User;
            user.Status = UserStatus.Active;

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
