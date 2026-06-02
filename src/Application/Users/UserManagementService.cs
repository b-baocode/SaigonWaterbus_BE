using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Interfaces;

namespace SaigonWaterbus.Application.Users;

public sealed class UserManagementService : IUserManagementService
{
    private readonly IRequestValidator _validator;
    private readonly GetUsersRequestUseCase _getUsers;
    private readonly GetUserByIdRequestUseCase _getUserById;
    private readonly GetManageableRolesRequestUseCase _getManageableRoles;
    private readonly CreateUserRequestUseCase _createUser;
    private readonly UpdateUserRequestUseCase _updateUser;
    private readonly UpdateUserStatusRequestUseCase _updateUserStatus;
    private readonly DeleteUserRequestUseCase _deleteUser;

    public UserManagementService(
        IRequestValidator validator,
        GetUsersRequestUseCase getUsers,
        GetUserByIdRequestUseCase getUserById,
        GetManageableRolesRequestUseCase getManageableRoles,
        CreateUserRequestUseCase createUser,
        UpdateUserRequestUseCase updateUser,
        UpdateUserStatusRequestUseCase updateUserStatus,
        DeleteUserRequestUseCase deleteUser)
    {
        _validator = validator;
        _getUsers = getUsers;
        _getUserById = getUserById;
        _getManageableRoles = getManageableRoles;
        _createUser = createUser;
        _updateUser = updateUser;
        _updateUserStatus = updateUserStatus;
        _deleteUser = deleteUser;
    }

    public async Task<IReadOnlyCollection<AuthUserDto>> GetUsersAsync(CancellationToken cancellationToken) =>
        await _getUsers.ExecuteAsync(new GetUsersRequest(), cancellationToken);

    public async Task<AuthUserDto> GetUserByIdAsync(int userId, CancellationToken cancellationToken)
    {
        var request = new GetUserByIdRequest(userId);
        await _validator.ValidateAsync(request, cancellationToken);
        return await _getUserById.ExecuteAsync(request, cancellationToken);
    }

    public async Task<IReadOnlyCollection<UserRoleDto>> GetManageableRolesAsync(CancellationToken cancellationToken) =>
        await _getManageableRoles.ExecuteAsync(new GetManageableRolesRequest(), cancellationToken);

    public async Task<AuthUserDto> CreateUserAsync(CreateUserRequest request, CancellationToken cancellationToken)
    {
        await _validator.ValidateAsync(request, cancellationToken);
        return await _createUser.ExecuteAsync(request, cancellationToken);
    }

    public async Task<AuthUserDto> UpdateUserAsync(UpdateUserRequest request, CancellationToken cancellationToken)
    {
        await _validator.ValidateAsync(request, cancellationToken);
        return await _updateUser.ExecuteAsync(request, cancellationToken);
    }

    public async Task<AuthUserDto> UpdateUserStatusAsync(UpdateUserStatusRequest request, CancellationToken cancellationToken)
    {
        await _validator.ValidateAsync(request, cancellationToken);
        return await _updateUserStatus.ExecuteAsync(request, cancellationToken);
    }

    public async Task<AuthActionResultDto> DeleteUserAsync(int userId, CancellationToken cancellationToken)
    {
        var request = new DeleteUserRequest(userId);
        await _validator.ValidateAsync(request, cancellationToken);
        return await _deleteUser.ExecuteAsync(request, cancellationToken);
    }
}
