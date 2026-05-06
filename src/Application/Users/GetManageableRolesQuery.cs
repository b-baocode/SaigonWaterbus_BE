using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Constants;

namespace SaigonWaterbus.Application.Users;

public sealed record GetManageableRolesQuery() : IRequest<IReadOnlyCollection<UserRoleDto>>;

public sealed record UserRoleDto(
    int Id,
    string Code,
    string SystemName,
    string DisplayName);

public sealed class GetManageableRolesQueryHandler : IRequestHandler<GetManageableRolesQuery, IReadOnlyCollection<UserRoleDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;

    public GetManageableRolesQueryHandler(
        IApplicationDbContext context,
        IUserContext userContext)
    {
        _context = context;
        _userContext = userContext;
    }

    public async Task<IReadOnlyCollection<UserRoleDto>> Handle(
        GetManageableRolesQuery request,
        CancellationToken cancellationToken)
    {
        var actor = await AuthSupport.EnsureCurrentUserCanManageUsersAsync(_context, _userContext, cancellationToken);

        var rolesQuery = _context.Roles.AsQueryable();
        if (AuthSupport.IsAdmin(actor))
        {
            rolesQuery = rolesQuery.Where(x => x.SystemName == Roles.ManagerSystemName);
        }
        else
        {
            rolesQuery = rolesQuery.Where(x => x.SystemName == Roles.StaffSystemName);
        }

        return await rolesQuery
            .OrderBy(x => x.Id)
            .Select(x => new UserRoleDto(x.Id, x.Code, x.SystemName, x.DisplayName))
            .ToArrayAsync(cancellationToken);
    }
}
