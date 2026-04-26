using SaigonWaterbus.Application.Users;
using SaigonWaterbus.Web.Infrastructure;

namespace SaigonWaterbus.Web.Endpoints;

public sealed class Users : IEndpointGroup
{
    public static string RoutePrefix => "/api/users";

    public static void Map(RouteGroupBuilder groupBuilder)
    {
        groupBuilder.MapGet(List, "")
            .RequireAuthorization();

        groupBuilder.MapGet(GetById, "{userId:int}")
            .RequireAuthorization();

        groupBuilder.MapPost(Create, "")
            .RequireAuthorization();

        groupBuilder.MapPut(Update, "{userId:int}")
            .RequireAuthorization();

        groupBuilder.MapDelete(Delete, "{userId:int}")
            .RequireAuthorization();
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
                request.Role,
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
                request.Role,
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
        string Role,
        string? Department,
        Domain.Enums.UserStatus? Status);

    public sealed record UpdateUserRequest(
        string FullName,
        DateOnly? DateOfBirth,
        string? PhoneNumber,
        string Email,
        string Role,
        string? Department,
        Domain.Enums.UserStatus Status);
}
