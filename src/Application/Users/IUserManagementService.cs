using SaigonWaterbus.Application.Auth.Common;

namespace SaigonWaterbus.Application.Users;

public interface IUserManagementService
{
    Task<IReadOnlyCollection<AuthUserDto>> GetUsersAsync(CancellationToken cancellationToken);

    Task<AuthUserDto> GetUserByIdAsync(Guid userId, CancellationToken cancellationToken);

    Task<IReadOnlyCollection<UserRoleDto>> GetManageableRolesAsync(CancellationToken cancellationToken);

    Task<AuthUserDto> CreateUserAsync(CreateUserRequest request, CancellationToken cancellationToken);

    Task<AuthUserDto> UpdateUserAsync(UpdateUserRequest request, CancellationToken cancellationToken);

    Task<AuthUserDto> UpdateUserStatusAsync(UpdateUserStatusRequest request, CancellationToken cancellationToken);

    Task<AuthActionResultDto> DeleteUserAsync(Guid userId, CancellationToken cancellationToken);
}
