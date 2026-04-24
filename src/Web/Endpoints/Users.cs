using SaigonWaterbus.Application.Users;
using SaigonWaterbus.Web.Infrastructure;

namespace SaigonWaterbus.Web.Endpoints;

public sealed class Users : IEndpointGroup
{
    public static string RoutePrefix => "/api/users";

    public static void Map(RouteGroupBuilder groupBuilder)
    {
        groupBuilder.MapPut(UpdateRole, "{userId:int}/role")
            .RequireAuthorization();

        groupBuilder.MapDelete(Delete, "{userId:int}")
            .RequireAuthorization();
    }

    private static async Task<IResult> UpdateRole(
        ISender sender,
        int userId,
        UpdateUserRoleRequest request,
        CancellationToken cancellationToken) =>
        Results.Ok(await sender.Send(new UpdateUserRoleCommand(userId, request.RoleCode), cancellationToken));

    private static async Task<IResult> Delete(
        ISender sender,
        int userId,
        CancellationToken cancellationToken) =>
        Results.Ok(await sender.Send(new DeleteUserCommand(userId), cancellationToken));

    public sealed record UpdateUserRoleRequest(string RoleCode);
}
