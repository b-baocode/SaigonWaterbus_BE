using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using SaigonWaterbus.Application.PushTokens;
using SaigonWaterbus.Web.Infrastructure;

namespace SaigonWaterbus.Web.Endpoints;

public sealed class PushTokens : IEndpointGroup
{
    public static string RoutePrefix => "/api/push-tokens";

    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost(Register, string.Empty)
            .RequireAuthorization()
            .WithSummary("Đăng ký Expo Push Token cho thiết bị hiện tại");

        group.MapDelete(Unregister, "{id:guid}")
            .RequireAuthorization()
            .WithSummary("Xóa push token (logout / đổi thiết bị)");

        group.MapPost(DisableAll, "disable-all")
            .RequireAuthorization()
            .WithSummary("Tắt tất cả push token của user hiện tại");
    }

    private static async Task<IResult> Register(
        ISender sender,
        RegisterPushTokenCommand command,
        CancellationToken ct) =>
        Results.Ok(await sender.Send(command, ct));

    private static async Task<IResult> Unregister(
        ISender sender,
        Guid id,
        CancellationToken ct) =>
        Results.Ok(await sender.Send(new UnregisterPushTokenCommand(id), ct));

    private static async Task<IResult> DisableAll(
        ISender sender,
        CancellationToken ct) =>
        Results.Ok(await sender.Send(new DisableMyPushTokensCommand(), ct));
}