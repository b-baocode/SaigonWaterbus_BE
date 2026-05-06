using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Application.Users;

public sealed record DeleteUserCommand(int UserId) : IRequest<AuthActionResultDto>;

public sealed class DeleteUserCommandValidator : AbstractValidator<DeleteUserCommand>
{
    public DeleteUserCommandValidator()
    {
        RuleFor(x => x.UserId).GreaterThan(0);
    }
}

public sealed class DeleteUserCommandHandler : IRequestHandler<DeleteUserCommand, AuthActionResultDto>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;
    private readonly TimeProvider _timeProvider;

    public DeleteUserCommandHandler(
        IApplicationDbContext context,
        IUserContext userContext,
        TimeProvider timeProvider)
    {
        _context = context;
        _userContext = userContext;
        _timeProvider = timeProvider;
    }

    public async Task<AuthActionResultDto> Handle(DeleteUserCommand request, CancellationToken cancellationToken)
    {
        var actor = await AuthSupport.EnsureCurrentUserCanManageUsersAsync(_context, _userContext, cancellationToken);
        var user = await _context.Set<User>()
            .Include(x => x.Role)
            .SingleOrDefaultAsync(x => x.Id == request.UserId, cancellationToken)
            ?? throw new global::SaigonWaterbus.Application.Common.Exceptions.NotFoundException("User was not found.");

        UserManagementSupport.EnsureCanDeleteUser(actor, user);

        var now = _timeProvider.GetUtcNow();
        await AuthSupport.RevokeActiveRefreshTokensAsync(_context, user.Id, now, cancellationToken);
        _context.AuditLogs.Add(UserAuditSupport.CreateUserAuditLog(
            actor.Id,
            AuditActions.DeleteUser,
            user.Id,
            UserAuditSupport.CreateUserSnapshot(user),
            newValues: null,
            now));
        _context.Set<User>().Remove(user);
        await _context.SaveChangesAsync(cancellationToken);

        return new AuthActionResultDto("Xoa user thanh cong.");
    }
}
