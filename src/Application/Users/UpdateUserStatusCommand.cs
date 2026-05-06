using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Application.Users;

public sealed record UpdateUserStatusCommand(
    int UserId,
    UserStatus Status) : IRequest<AuthUserDto>;

public sealed class UpdateUserStatusCommandValidator : AbstractValidator<UpdateUserStatusCommand>
{
    public UpdateUserStatusCommandValidator()
    {
        RuleFor(x => x.UserId).GreaterThan(0);

        RuleFor(x => x.Status)
            .IsInEnum()
            .WithMessage("Trạng thái user không hợp lệ.")
            .Must(x => x is UserStatus.Active or UserStatus.Suspended)
            .WithMessage("Management API chỉ được đổi trạng thái sang Active hoặc Suspended.");
    }
}

public sealed class UpdateUserStatusCommandHandler : IRequestHandler<UpdateUserStatusCommand, AuthUserDto>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;
    private readonly TimeProvider _timeProvider;

    public UpdateUserStatusCommandHandler(
        IApplicationDbContext context,
        IUserContext userContext,
        TimeProvider timeProvider)
    {
        _context = context;
        _userContext = userContext;
        _timeProvider = timeProvider;
    }

    public async Task<AuthUserDto> Handle(UpdateUserStatusCommand request, CancellationToken cancellationToken)
    {
        var actor = await AuthSupport.EnsureCurrentUserCanManageUsersAsync(_context, _userContext, cancellationToken);
        var user = await _context.Set<User>()
            .Include(x => x.Role)
            .SingleOrDefaultAsync(x => x.Id == request.UserId, cancellationToken)
            ?? throw new SaigonWaterbus.Application.Common.Exceptions.NotFoundException("User was not found.");

        UserManagementSupport.EnsureCanUpdateUser(actor, user);

        var oldValues = UserAuditSupport.CreateUserSnapshot(user);
        user.Status = request.Status;

        if (user.Status == UserStatus.Active
            && user.NormalizedPhoneNumber is not null
            && user.PhoneVerifiedAt is null)
        {
            user.PhoneVerifiedAt = _timeProvider.GetUtcNow();
        }

        await AuthSupport.RevokeActiveRefreshTokensAsync(_context, user.Id, _timeProvider.GetUtcNow(), cancellationToken);
        _context.AuditLogs.Add(UserAuditSupport.CreateUserAuditLog(
            actor.Id,
            AuditActions.UpdateUser,
            user.Id,
            oldValues,
            UserAuditSupport.CreateUserSnapshot(user),
            _timeProvider.GetUtcNow()));
        await _context.SaveChangesAsync(cancellationToken);

        return AuthSupport.CreateUserDto(user);
    }
}
