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
            .WithSummary("Đăng ký Expo Push Token cho thiết bị hiện tại")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Bearer token",
                "application/json",
                "FE gọi sau khi user đăng nhập và Expo SDK đã cấp token.",
                "Idempotent: nếu token đã tồn tại → trả về id cũ và AlreadyRegistered=true.",
                "Tự động deactivate token cũ của cùng (user, deviceId) nếu có.",
                "Body: { expoPushToken, platform: 'Ios'|'Android', deviceId?, appVersion? }",
                "Response: { id, alreadyRegistered }"));

        group.MapDelete(Unregister, "{id:guid}")
            .RequireAuthorization()
            .WithSummary("Xóa push token (logout / đổi thiết bị)")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Bearer token",
                null,
                "Xóa cứng token khỏi DB. Trả 404 nếu không thuộc user hiện tại."));

        group.MapPost(DisableAll, "disable-all")
            .RequireAuthorization()
            .WithSummary("Tắt tất cả push token của user hiện tại")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Bearer token",
                null,
                "Soft disable: set is_active=false. Dùng khi user tắt notification trong app settings.",
                "Trả về số token vừa bị disable."));
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
