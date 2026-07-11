using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Application.Users;

public sealed record GetUsersRequest(
    StaffType? StaffType = null,
    UserStatus? Status = null);

public sealed class GetUsersRequestUseCase
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;

    public GetUsersRequestUseCase(
        IApplicationDbContext context,
        IUserContext userContext)
    {
        _context = context;
        _userContext = userContext;
    }

    public async Task<IReadOnlyCollection<AuthUserDto>> ExecuteAsync(GetUsersRequest request, CancellationToken cancellationToken)
    {
        var actor = await AuthSupport.EnsureCurrentUserCanManageUsersAsync(_context, _userContext, cancellationToken);

        var query = UserManagementSupport.BuildVisibleUsersQuery(_context, actor);
        if (request.StaffType.HasValue)
        {
            query = query.Where(x => x.StaffType == request.StaffType.Value);
        }

        if (request.Status.HasValue)
        {
            query = query.Where(x => x.Status == request.Status.Value);
        }

        var users = await query
            .OrderBy(x => x.FullName)
            .ThenBy(x => x.Id)
            .ToListAsync(cancellationToken);

        return users
            .Select(AuthSupport.CreateUserDto)
            .ToArray();
    }
}
