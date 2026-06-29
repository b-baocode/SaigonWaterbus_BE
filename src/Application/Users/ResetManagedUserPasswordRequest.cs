using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Application.Users;

public sealed record ResetManagedUserPasswordRequest(Guid UserId);

public sealed class ResetManagedUserPasswordRequestValidator : AbstractValidator<ResetManagedUserPasswordRequest>
{
    public ResetManagedUserPasswordRequestValidator()
    {
        RuleFor(x => x.UserId)
            .NotEmpty()
            .WithMessage("UserId không hợp lệ.");
    }
}

public sealed class ResetManagedUserPasswordRequestUseCase
{
    private readonly IApplicationDbContext _context;
    private readonly ISecretHasher _secretHasher;
    private readonly IUserContext _userContext;
    private readonly TimeProvider _timeProvider;

    public ResetManagedUserPasswordRequestUseCase(
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

    public async Task<ManagedUserPasswordResultDto> ExecuteAsync(
        ResetManagedUserPasswordRequest request,
        CancellationToken cancellationToken)
    {
        var actor = await AuthSupport.EnsureCurrentUserCanManageUsersAsync(_context, _userContext, cancellationToken);
        var user = await _context.Set<User>()
            .Include(x => x.Role)
            .Include(x => x.StationAssignments).ThenInclude(a => a.Station)
            .SingleOrDefaultAsync(x => x.Id == request.UserId, cancellationToken)
            ?? throw new SaigonWaterbus.Application.Common.Exceptions.NotFoundException("Không tìm thấy user.");

        UserManagementSupport.EnsureCanResetManagedPassword(actor, user);

        var generatedPassword = ManagedUserPasswordSupport.GeneratePassword();
        var now = _timeProvider.GetUtcNow();
        user.PasswordHash = _secretHasher.Hash(generatedPassword);
        user.FailedLoginAttemptCount = 0;
        user.FailedLoginWindowStartedAt = null;

        await AuthSupport.RevokeActiveRefreshTokensAsync(_context, user.Id, now, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);

        return new ManagedUserPasswordResultDto(AuthSupport.CreateUserDto(user), generatedPassword);
    }
}
