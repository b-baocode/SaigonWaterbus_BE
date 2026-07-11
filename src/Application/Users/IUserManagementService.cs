using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Application.Users;

public interface IUserManagementService
{
    Task<IReadOnlyCollection<AuthUserDto>> GetUsersAsync(
        StaffType? staffType,
        UserStatus? status,
        CancellationToken cancellationToken);

    Task<AuthUserDto> GetUserByIdAsync(Guid userId, CancellationToken cancellationToken);

    Task<IReadOnlyCollection<UserRoleDto>> GetManageableRolesAsync(CancellationToken cancellationToken);

    Task<ManagedUserPasswordResultDto> CreateUserAsync(CreateUserRequest request, CancellationToken cancellationToken);

    Task<AuthUserDto> UpdateUserAsync(UpdateUserRequest request, CancellationToken cancellationToken);

    Task<AuthUserDto> UpdateUserStatusAsync(UpdateUserStatusRequest request, CancellationToken cancellationToken);

    Task<ManagedUserPasswordResultDto> ResetManagedUserPasswordAsync(
        Guid userId,
        CancellationToken cancellationToken);

    Task<IReadOnlyCollection<UserStationAssignmentDto>> GetUserStationAssignmentsAsync(
        Guid userId,
        CancellationToken cancellationToken);

    Task<IReadOnlyCollection<UserStationAssignmentDto>> AssignUserStationsAsync(
        AssignUserStationsRequest request,
        CancellationToken cancellationToken);

    Task<AuthActionResultDto> DeleteUserAsync(Guid userId, CancellationToken cancellationToken);
}
