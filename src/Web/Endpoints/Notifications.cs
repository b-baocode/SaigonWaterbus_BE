using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Application.Notifications;

namespace SaigonWaterbus.Web.Endpoints;

public sealed class Notifications : IEndpointGroup
{
    public static string RoutePrefix => "/api/notifications";

    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet(GetMyNotifications, string.Empty)
            .RequireAuthorization()
            .WithSummary("Danh sach thong bao cua toi")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Bearer token",
                null,
                "Tra ve thong bao cua user dang dang nhap, moi nhat truoc, kem totalCount/unreadCount.",
                "Staff, Manager, Admin va Customer deu dung chung endpoint nay; server tu loc theo userId trong access token.",
                "Query: page (mac dinh 1), pageSize (mac dinh 20, toi da 100), unreadOnly (mac dinh false).",
                "type: loc theo loai notification (vd: booking_confirmed, trip_cancelled).",
                "relatedEntityType: loc theo loai entity lien ket (booking | trip | incident | promotion | staff_assignment).",
                "relatedEntityId: loc theo id cua entity cu the.",
                "unreadCount luon la tong so chua doc, khong phu thuoc unreadOnly — dung cho badge chuong.",
                "type + relatedEntityType/relatedEntityId dung de dieu huong khi bam vao thong bao."));

        group.MapPost(MarkReadByFilter, "read-by-filter")
            .RequireAuthorization()
            .WithSummary("Danh dau da doc theo bo loc")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Bearer token",
                null,
                "Danh dau da doc tat ca notification cua user hien tai theo bo loc (type / relatedEntityType / relatedEntityId).",
                "Tra ve markedCount = so thong bao vua duoc danh dau.",
                "unreadOnly mac dinh la true (chi danh dau nhung thong bao chua doc).",
                "Su dung khi user bam nut 'Doc het' tren mot tab nhat dinh (vd: tab Booking)."));

        group.MapGet(GetUnreadCount, "unread-count")
            .RequireAuthorization()
            .WithSummary("So thong bao chua doc")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Bearer token",
                null,
                "Goi luc mo app de ve badge chuong; sau do cap nhat qua SignalR /hubs/notifications."));

        group.MapPost(MarkRead, "{id:guid}/read")
            .RequireAuthorization()
            .WithSummary("Danh dau da doc mot thong bao")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Bearer token",
                null,
                "Idempotent: goi lai voi thong bao da doc thi giu nguyen readAt cu.",
                "Thong bao cua user khac tra ve 404."));

        group.MapPost(MarkAllRead, "read-all")
            .RequireAuthorization()
            .WithSummary("Danh dau da doc tat ca")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Bearer token",
                null,
                "Tra ve markedCount = so thong bao vua duoc danh dau."));
    }

    private static async Task<IResult> GetMyNotifications(
        ISender sender,
        int? page,
        int? pageSize,
        bool? unreadOnly,
        string? type,
        string? relatedEntityType,
        Guid? relatedEntityId,
        CancellationToken ct) =>
        Results.Ok(await sender.Send(new GetMyNotificationsQuery(
            page ?? 1,
            pageSize ?? 20,
            unreadOnly ?? false,
            type,
            relatedEntityType,
            relatedEntityId), ct));

    private static async Task<IResult> GetUnreadCount(ISender sender, CancellationToken ct) =>
        Results.Ok(await sender.Send(new GetMyUnreadNotificationCountQuery(), ct));

    private static async Task<IResult> MarkRead(ISender sender, Guid id, CancellationToken ct) =>
        Results.Ok(await sender.Send(new MarkNotificationReadCommand(id), ct));

    private static async Task<IResult> MarkAllRead(ISender sender, CancellationToken ct) =>
        Results.Ok(await sender.Send(new MarkAllMyNotificationsReadCommand(), ct));

    private static async Task<IResult> MarkReadByFilter(
        ISender sender,
        string? type,
        string? relatedEntityType,
        Guid? relatedEntityId,
        bool? unreadOnly,
        CancellationToken ct) =>
        Results.Ok(await sender.Send(new MarkNotificationsReadByFilterCommand(
            type, relatedEntityType, relatedEntityId, unreadOnly ?? true), ct));
}
