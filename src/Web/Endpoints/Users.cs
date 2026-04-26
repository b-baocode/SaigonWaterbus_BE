using SaigonWaterbus.Application.Users;
using SaigonWaterbus.Web.Infrastructure;

namespace SaigonWaterbus.Web.Endpoints;

public sealed class Users : IEndpointGroup
{
    public static string RoutePrefix => "/api/users";

    public static void Map(RouteGroupBuilder groupBuilder)
    {
        groupBuilder.MapGet(List, "")
            .RequireAuthorization()
            .WithSummary("Lay danh sach user")
            .WithDescription(
                "Quyen truy cap: Manager va Admin System.\n" +
                "- Admin System: xem tat ca user.\n" +
                "- Manager: chi xem duoc Customer va Staff.\n" +
                "API Note:\n" +
                "- Header: Authorization: Bearer <accessToken>.\n" +
                "- Dung endpoint nay de lay danh sach user va doc roleId hien tai truoc khi update.");

        groupBuilder.MapGet(GetById, "{userId:int}")
            .RequireAuthorization()
            .WithSummary("Lay chi tiet user theo ID")
            .WithDescription(
                "Quyen truy cap: Manager va Admin System.\n" +
                "- Admin System: xem tat ca user.\n" +
                "- Manager: chi xem duoc Customer va Staff.\n" +
                "API Note:\n" +
                "- Header: Authorization: Bearer <accessToken>.\n" +
                "- Truyen userId tren route de lay 1 user cu the.");

        groupBuilder.MapPost(Create, "")
            .RequireAuthorization()
            .WithSummary("Tao user moi")
            .WithDescription(
                "Quyen truy cap: Manager va Admin System.\n" +
                "- Admin System: tao duoc Customer, Staff, Manager, Admin System.\n" +
                "- Manager: chi tao duoc Staff.\n" +
                "API Note:\n" +
                "- Header: Authorization: Bearer <accessToken>.\n" +
                "- Body dung roleId, khong dung role code/string.\n" +
                "- roleId lay tu bang roles.\n" +
                "- Department chi can cho internal role, khong can cho Customer.");

        groupBuilder.MapPut(Update, "{userId:int}")
            .RequireAuthorization()
            .WithSummary("Cap nhat user")
            .WithDescription(
                "Quyen truy cap: Manager va Admin System.\n" +
                "- Admin System: cap nhat duoc user bat ky (tru chinh minh).\n" +
                "- Manager: chi cap nhat duoc Customer va role phai giu nguyen la Customer.\n" +
                "API Note:\n" +
                "- Header: Authorization: Bearer <accessToken>.\n" +
                "- Route: userId.\n" +
                "- Body dung roleId moi neu can doi role.\n" +
                "- Neu doi sang role co prefix code khac, backend se cap lai userCode.");

        groupBuilder.MapDelete(Delete, "{userId:int}")
            .RequireAuthorization()
            .WithSummary("Xoa user")
            .WithDescription(
                "Quyen truy cap: Manager va Admin System.\n" +
                "- Admin System: xoa duoc user bat ky (tru chinh minh).\n" +
                "- Manager: chi xoa duoc Customer.\n" +
                "API Note:\n" +
                "- Header: Authorization: Bearer <accessToken>.\n" +
                "- Route: userId.\n" +
                "- Nen goi GET truoc neu can xac nhan dung user.");
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
