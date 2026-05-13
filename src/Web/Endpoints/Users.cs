using SaigonWaterbus.Application.Users;
using SaigonWaterbus.Web.Infrastructure;

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
          "roleId": 3,
          "department": "Operations"
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
            .WithSummary("Lay danh sach role co the gan")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Manager hoac Admin System",
                null,
                "Dung API nay de biet roleId nao tuong ung role nao trong database.",
                "Admin System thay role Manager. Manager thay role Staff."));

        groupBuilder.MapGet(List, "")
            .RequireAuthorization()
            .WithSummary("Lay danh sach user")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Manager hoac Admin System",
                null,
                "Lay danh sach user theo quyen.",
                "Manager chi thay Customer va Staff."));

        groupBuilder.MapGet(GetById, "detail/{userId:int}")
            .RequireAuthorization()
            .WithSummary("Lay chi tiet user theo ID")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Manager hoac Admin System",
                null,
                "Lay chi tiet user theo id.",
                "Manager chi thay Customer va Staff."));

        groupBuilder.MapPost(CreateManagedUser, "managed")
            .RequireAuthorization()
            .WithSummary("Tao user moi tu man hinh quan ly")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Manager hoac Admin System",
                CreateUserExample,
                "Admin System chi tao duoc Manager.",
                "Manager chi tao duoc Staff. Customer dung flow /api/auth/register de tu dang ky va xac thuc OTP.",
                "Khong can truyen status khi tao user; he thong mac dinh tao user Active.",
                "RoleId khong co dinh theo code. Goi GET /api/users/roles de xem id hien tai."));

        groupBuilder.MapPut(Update, "update/{userId:int}")
            .RequireAuthorization()
            .WithSummary("Cap nhat user")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Manager hoac Admin System",
                UpdateUserExample,
                "Cap nhat user theo quyen.",
                "Manager chi cap nhat Customer va Staff."));

        groupBuilder.MapPatch(UpdateStatus, "status/{userId:int}")
            .RequireAuthorization()
            .WithSummary("Cap nhat trang thai user")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Manager hoac Admin System",
                UpdateStatusExample,
                "Status hop le: Active, Suspended.",
                "Doi status se revoke refresh token dang hoat dong cua user."));

        groupBuilder.MapDelete(Delete, "delete/{userId:int}")
            .RequireAuthorization()
            .WithSummary("Xoa user")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Manager hoac Admin System",
                null,
                "Xoa user theo quyen.",
                "Customer tu xoa bang DELETE /api/auth/profile."));

        groupBuilder.MapGet(GetById, "{userId:int}")
            .RequireAuthorization()
            .WithName("GetUserByIdLegacy")
            .ExcludeFromDescription();

        groupBuilder.MapPut(Update, "{userId:int}")
            .RequireAuthorization()
            .WithName("UpdateUserLegacy")
            .ExcludeFromDescription();

        groupBuilder.MapDelete(Delete, "{userId:int}")
            .RequireAuthorization()
            .WithName("DeleteUserLegacy")
            .ExcludeFromDescription();
    }

    private static async Task<IResult> List(
        ISender sender,
        CancellationToken cancellationToken) =>
        Results.Ok(await sender.Send(new GetUsersQuery(), cancellationToken));

    private static async Task<IResult> GetById(
        ISender sender,
        int userId,
        CancellationToken cancellationToken) =>
        Results.Ok(await sender.Send(new GetUserByIdQuery(userId), cancellationToken));

    private static async Task<IResult> GetManageableRoles(
        ISender sender,
        CancellationToken cancellationToken) =>
        Results.Ok(await sender.Send(new GetManageableRolesQuery(), cancellationToken));

    private static async Task<IResult> CreateManagedUser(
        ISender sender,
        CreateUserRequest request,
        CancellationToken cancellationToken) =>
        Results.Ok(await sender.Send(
            new CreateUserCommand(
                request.FullName,
                request.DateOfBirth,
                request.PhoneNumber,
                request.Email,
                request.Password,
                request.RoleId,
                request.Department),
            cancellationToken));

    private static async Task<IResult> Update(
        ISender sender,
        int userId,
        UpdateUserRequest request,
        CancellationToken cancellationToken) =>
        Results.Ok(await sender.Send(
            new UpdateUserCommand(
                userId,
                request.FullName,
                request.DateOfBirth,
                request.PhoneNumber,
                request.Email,
                request.RoleId,
                request.Department),
            cancellationToken));

    private static async Task<IResult> UpdateStatus(
        ISender sender,
        int userId,
        UpdateUserStatusRequest request,
        CancellationToken cancellationToken) =>
        Results.Ok(await sender.Send(
            new UpdateUserStatusCommand(
                userId,
                request.Status),
            cancellationToken));

    private static async Task<IResult> Delete(
        ISender sender,
        int userId,
        CancellationToken cancellationToken) =>
        Results.Ok(await sender.Send(new DeleteUserCommand(userId), cancellationToken));

    public sealed record CreateUserRequest(
        string FullName,
        DateOnly? DateOfBirth,
        string? PhoneNumber,
        string Email,
        string Password,
        int RoleId,
        string? Department);

    public sealed record UpdateUserRequest(
        string? FullName = null,
        DateOnly? DateOfBirth = null,
        string? PhoneNumber = null,
        string? Email = null,
        int? RoleId = null,
        string? Department = null);

    public sealed record UpdateUserStatusRequest(
        Domain.Enums.UserStatus Status);
}
