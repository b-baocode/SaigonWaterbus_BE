using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Application.Auth.Password;

public sealed record ChangePasswordCommand(string CurrentPassword, string NewPassword) : IRequest<AuthActionResultDto>;

public sealed class ChangePasswordCommandValidator : AbstractValidator<ChangePasswordCommand>
{
    public ChangePasswordCommandValidator()
    {
        RuleFor(x => x.CurrentPassword)
            .NotEmpty();

        RuleFor(x => x.NewPassword)
            .NotEmpty()
            .MinimumLength(8)
            .NotEqual(x => x.CurrentPassword)
            .WithMessage("New password must be different from current password.");
    }
}

public sealed class ChangePasswordCommandHandler : IRequestHandler<ChangePasswordCommand, AuthActionResultDto>
{
    private readonly IApplicationDbContext _context;
    private readonly ISecretHasher _secretHasher;
    private readonly IUserContext _userContext;
    private readonly TimeProvider _timeProvider;

    public ChangePasswordCommandHandler(
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

    public async Task<AuthActionResultDto> Handle(ChangePasswordCommand request, CancellationToken cancellationToken)
    {
        if (!_userContext.UserId.HasValue)
        {
            throw new UnauthorizedAccessException();
        }

        var user = await _context.Set<User>()
            .SingleOrDefaultAsync(x => x.Id == _userContext.UserId.Value, cancellationToken)
            ?? throw new global::SaigonWaterbus.Application.Common.Exceptions.NotFoundException("User was not found.");

        AuthSupport.EnsureUserCanLogin(user);

        if (string.IsNullOrWhiteSpace(user.PasswordHash))
        {
            throw AuthSupport.CreateValidationException(nameof(request.CurrentPassword), "Password login is not available for this account.");
        }

        if (!_secretHasher.Verify(request.CurrentPassword, user.PasswordHash))
        {
            throw AuthSupport.CreateValidationException(nameof(request.CurrentPassword), "Current password is incorrect.");
        }

        if (_secretHasher.Verify(request.NewPassword, user.PasswordHash))
        {
            throw AuthSupport.CreateValidationException(nameof(request.NewPassword), "New password must be different from current password.");
        }

        var now = _timeProvider.GetUtcNow();
        user.PasswordHash = _secretHasher.Hash(request.NewPassword);

        await AuthSupport.RevokeActiveRefreshTokensAsync(_context, user.Id, now, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);

        return new AuthActionResultDto("Doi mat khau thanh cong.");
    }
}
