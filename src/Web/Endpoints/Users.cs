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
          "email": "thib@fpt.edu.vn",
          "password": "P@ssword123",
          "roleId": 3,
          "department": "Operations",
          "status": "Active"
        }
        """;

    private const string UpdateUserExample =
        """
        {
          "fullName": "Tran Thi B Updated",
          "dateOfBirth": "10/05/1998",
          "phoneNumber": "0912345678",
          "email": "thib@fpt.edu.vn",
          "roleId": 4,
          "department": null,
          "status": "Active"
        }
        """;

    public static string RoutePrefix => "/api/users";

    public static void Map(RouteGroupBuilder groupBuilder)
    {
        groupBuilder.MapGet(List, "")
            .RequireAuthorization()
            .WithSummary("Lay danh sach user")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Manager hoac Admin System",
                null,
                "Header can co Authorization: Bearer <accessToken>.",
                "Manager chi xem duoc Customer va Staff.",
                "Dung de doc roleId hien tai truoc khi update."));

        groupBuilder.MapGet(GetById, "{userId:int}")
            .RequireAuthorization()
            .WithSummary("Lay chi tiet user theo ID")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Manager hoac Admin System",
                null,
                "Header can co Authorization: Bearer <accessToken>.",
                "Example route: /api/users/12.",
                "Manager chi xem duoc Customer va Staff."));

        groupBuilder.MapPost(Create, "")
            .RequireAuthorization()
            .WithSummary("Tao user moi")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Manager hoac Admin System",
                CreateUserExample,
                "Header can co Authorization: Bearer <accessToken>.",
                "Body dung roleId, khong dung role code.",
                "Department chi can cho internal role.",
                "Manager chi tao duoc Customer va Staff."));

        groupBuilder.MapPut(Update, "{userId:int}")
            .RequireAuthorization()
            .WithSummary("Cap nhat user")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Manager hoac Admin System",
                UpdateUserExample,
                "Header can co Authorization: Bearer <accessToken>.",
                "Example route: /api/users/12.",
                "Body dung roleId moi neu can doi role.",
                "Neu doi sang role co prefix khac, backend se cap lai userCode.",
                "Manager chi cap nhat duoc Customer va Staff."));

        groupBuilder.MapDelete(Delete, "{userId:int}")
            .RequireAuthorization()
            .WithSummary("Xoa user")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Manager hoac Admin System",
                null,
                "Header can co Authorization: Bearer <accessToken>.",
                "Example route: /api/users/12.",
                "Admin System chi xoa duoc Manager va Staff.",
                "Manager chi xoa duoc Customer va Staff.",
                "Customer tu xoa tai khoan bang DELETE /api/auth/me."));
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

    private static async Task<IResult> Create(
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
                request.Department,
                request.Status),
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
                request.Department,
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
        string? Department,
        Domain.Enums.UserStatus? Status);

    public sealed record UpdateUserRequest(
        string FullName,
        DateOnly? DateOfBirth,
        string? PhoneNumber,
        string Email,
        int RoleId,
        string? Department,
        Domain.Enums.UserStatus Status);
}
