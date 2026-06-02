using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Interfaces;

namespace SaigonWaterbus.Application.Users;

public sealed record GetUsersRequest();

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

        var users = await UserManagementSupport.BuildVisibleUsersQuery(_context, actor)
            .OrderBy(x => x.FullName)
            .ThenBy(x => x.Id)
            .ToListAsync(cancellationToken);

        return users
            .Select(AuthSupport.CreateUserDto)
            .ToArray();
    }
}
