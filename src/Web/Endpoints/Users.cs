using SaigonWaterbus.Application.Users;

namespace SaigonWaterbus.Web.Endpoints;

public sealed class Users : IEndpointGroup
{
    private const string CreateUserExample =
        """
        {
          "fullName": "Tran Thi B",
          "dateOfBirth": "10/05/1998",
          "phoneNumber": "0912345678",
          "email": "thib@gmail.com",
          "password": "P@ssword123",
          "roleId": 3
        }
        """;

    private const string UpdateStatusExample =
        """
        {
          "status": "Suspended"
        }
        """;

    private const string UpdateUserExample =
        """
        {
          "phoneNumber": "0912345678"
        }
        """;

    public static string RoutePrefix => "/api/users";

    public static string OpenApiTag => "Users";

    public static void Map(RouteGroupBuilder groupBuilder)
    {
        groupBuilder.MapGet(GetManageableRoles, "roles")
            .RequireAuthorization()
            .WithSummary("Lấy danh sách role có thể gán")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Manager hoặc Admin",
                null,
                "Dùng API này để biết roleId nào tương ứng role nào trong database.",
                "Admin thấy role Manager. Manager thấy role Staff."));

        groupBuilder.MapGet(List, "")
            .RequireAuthorization()
            .WithSummary("Lấy danh sách user")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Manager hoặc Admin",
                null,
                "Lấy danh sách user theo quyền.",
                "Manager chỉ thấy Customer và Staff."));

        groupBuilder.MapGet(GetById, "detail/{userId:guid}")
            .RequireAuthorization()
            .WithSummary("Lấy chi tiết user theo ID")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Manager hoặc Admin",
                null,
                "Lấy chi tiết user theo id.",
                "Manager chỉ thấy Customer và Staff."));

        groupBuilder.MapPost(CreateManagedUser, "managed")
            .RequireAuthorization()
            .WithSummary("Tạo user mới từ màn hình quản lý")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Manager hoặc Admin",
                CreateUserExample,
                "Admin chỉ tạo được Manager.",
                "Manager chỉ tạo được Staff. Customer dùng flow /api/auth/register để tự đăng ký và xác thực OTP.",
                "Không cần truyền status khi tạo user; hệ thống mặc định tạo user Active.",
                "RoleId không cố định theo code. Gọi GET /api/users/roles để xem id hiện tại."));

        groupBuilder.MapPut(Update, "update/{userId:guid}")
            .RequireAuthorization()
            .WithSummary("Cập nhật user")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Manager hoặc Admin",
                UpdateUserExample,
                "Cập nhật user theo quyền.",
                "Manager chỉ cập nhật Customer và Staff."));

        groupBuilder.MapPatch(UpdateStatus, "status/{userId:guid}")
            .RequireAuthorization()
            .WithSummary("Cập nhật trạng thái user")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Manager hoặc Admin",
                UpdateStatusExample,
                "Status hợp lệ: Active, Suspended.",
                "Đổi status sẽ revoke refresh token đang hoạt động của user."));

        groupBuilder.MapDelete(Delete, "delete/{userId:guid}")
            .RequireAuthorization()
            .WithSummary("Xóa user")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Manager hoặc Admin",
                null,
                "Xóa user theo quyền.",
                "Customer tự xóa bằng DELETE /api/auth/me."));

        groupBuilder.MapGet(GetById, "{userId:guid}")
            .RequireAuthorization()
            .WithName("GetUserByIdLegacy")
            .ExcludeFromDescription();

        groupBuilder.MapPut(Update, "{userId:guid}")
            .RequireAuthorization()
            .WithName("UpdateUserLegacy")
            .ExcludeFromDescription();

        groupBuilder.MapDelete(Delete, "{userId:guid}")
            .RequireAuthorization()
            .WithName("DeleteUserLegacy")
            .ExcludeFromDescription();
    }

    private static async Task<IResult> List(
        IUserManagementService userManagementService,
        CancellationToken cancellationToken) =>
        Results.Ok(await userManagementService.GetUsersAsync(cancellationToken));

    private static async Task<IResult> GetById(
        IUserManagementService userManagementService,
        Guid userId,
        CancellationToken cancellationToken) =>
        Results.Ok(await userManagementService.GetUserByIdAsync(userId, cancellationToken));

    private static async Task<IResult> GetManageableRoles(
        IUserManagementService userManagementService,
        CancellationToken cancellationToken) =>
        Results.Ok(await userManagementService.GetManageableRolesAsync(cancellationToken));

    private static async Task<IResult> CreateManagedUser(
        IUserManagementService userManagementService,
        CreateUserApiRequest request,
        CancellationToken cancellationToken) =>
        Results.Ok(await userManagementService.CreateUserAsync(
            new CreateUserRequest(
                request.FullName,
                request.DateOfBirth,
                request.PhoneNumber,
                request.Email,
                request.Password,
                request.RoleId),
            cancellationToken));

    private static async Task<IResult> Update(
        IUserManagementService userManagementService,
        Guid userId,
        UpdateUserApiRequest request,
        CancellationToken cancellationToken) =>
        Results.Ok(await userManagementService.UpdateUserAsync(
            new UpdateUserRequest(
                userId,
                request.FullName,
                request.DateOfBirth,
                request.PhoneNumber,
                request.Email,
                request.RoleId),
            cancellationToken));

    private static async Task<IResult> UpdateStatus(
        IUserManagementService userManagementService,
        Guid userId,
        UpdateUserStatusApiRequest request,
        CancellationToken cancellationToken) =>
        Results.Ok(await userManagementService.UpdateUserStatusAsync(
            new UpdateUserStatusRequest(
                userId,
                request.Status),
            cancellationToken));

    private static async Task<IResult> Delete(
        IUserManagementService userManagementService,
        Guid userId,
        CancellationToken cancellationToken) =>
        Results.Ok(await userManagementService.DeleteUserAsync(userId, cancellationToken));

    public sealed record CreateUserApiRequest(
        string FullName,
        DateOnly? DateOfBirth,
        string? PhoneNumber,
        string Email,
        string Password,
        Guid RoleId);

    public sealed record UpdateUserApiRequest(
        string? FullName = null,
        DateOnly? DateOfBirth = null,
        string? PhoneNumber = null,
        string? Email = null,
        Guid? RoleId = null);

    public sealed record UpdateUserStatusApiRequest(
        Domain.Enums.UserStatus Status);
}
