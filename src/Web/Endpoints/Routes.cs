using SaigonWaterbus.Application.Routes;

namespace SaigonWaterbus.Web.Endpoints;

public sealed class Routes : IEndpointGroup
{
    public static string RoutePrefix => "/api/routes";

    private const string CreateRouteExample =
        """
        {
          "routeCode": "R02-BD-NVL",
          "routeName": "Tuyen 02: Bach Dang → Nguyen Van Linh",
          "description": "Tuyen waterbus Q1 → Q7",
          "baseDistanceKm": 15.5,
          "estimatedDurationMin": 75
        }
        """;

    private const string UpdateRouteExample =
        """
        {
          "routeName": "Tuyen 02: Bach Dang → Nguyen Van Linh (cap nhat)",
          "description": "Mo ta moi",
          "baseDistanceKm": 16.0,
          "estimatedDurationMin": 80,
          "status": "Active"
        }
        """;

    private const string AddStopExample =
        """
        {
          "stationId": "86015ba6-adac-4fd3-9e5f-30eeb9b2cefe",
          "stopOrder": 1,
          "standardTravelMin": 8,
          "standardDwellMin": 2,
          "isPickupAllowed": true,
          "isDropoffAllowed": false
        }
        """;

    private const string UpdateStopExample =
        """
        {
          "standardTravelMin": 10,
          "standardDwellMin": 3,
          "isPickupAllowed": true,
          "isDropoffAllowed": true
        }
        """;

    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet(GetRoutes, string.Empty)
            .AllowAnonymous()
            .WithSummary("Danh sach tuyen")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Anonymous",
                null,
                "Tra ve tat ca tuyen co status = Active.",
                "Sap xep theo RouteCode."));

        group.MapGet(GetRouteById, "{id:guid}")
            .AllowAnonymous()
            .WithSummary("Chi tiet tuyen (kem danh sach ben dung)")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Anonymous",
                null,
                "Tra ve RouteDetailDto kem stops[] sap xep theo stop_order.",
                "Tra ve 404 neu khong tim thay tuyen."));

        group.MapPost(CreateRoute, string.Empty)
            .RequireAuthorization()
            .WithSummary("Tao tuyen moi")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Bearer token",
                CreateRouteExample,
                "RouteCode phai unique (tu dong uppercase).",
                "Status mac dinh la Active khi tao moi.",
                "Them ben dung bang POST /api/routes/{id}/stops."));

        group.MapPut(UpdateRoute, "{id:guid}")
            .RequireAuthorization()
            .WithSummary("Cap nhat thong tin tuyen")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Bearer token",
                UpdateRouteExample,
                "Status hop le: Active | Inactive.",
                "RouteCode khong doi duoc sau khi tao."));

        group.MapPost(AddRouteStop, "{id:guid}/stops")
            .RequireAuthorization()
            .WithSummary("Them ben dung vao tuyen")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Bearer token",
                AddStopExample,
                "stopOrder phai unique trong cung mot tuyen.",
                "stop cuoi (isDropoffAllowed=true, isPickupAllowed=false).",
                "stop dau (isPickupAllowed=true, isDropoffAllowed=false).",
                "standardTravelMin: phut di tu stop nay den stop tiep theo.",
                "standardDwellMin: phut tau dung tai stop (default 2)."));

        group.MapPut(UpdateRouteStop, "{id:guid}/stops/{stopId:guid}")
            .RequireAuthorization()
            .WithSummary("Cap nhat ben dung")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Bearer token",
                UpdateStopExample,
                "Khong doi duoc stationId hay stopOrder sau khi tao.",
                "Dung API nay de chinh thoi gian di chuyen va quyen len/xuong."));

        group.MapDelete(RemoveRouteStop, "{id:guid}/stops/{stopId:guid}")
            .RequireAuthorization()
            .WithSummary("Xoa ben dung khoi tuyen")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Bearer token",
                null,
                "Tra ve 204 khi xoa thanh cong.",
                "Tra ve 404 neu khong tim thay tuyen hoac stop."));
    }

    private static async Task<IResult> GetRoutes(ISender sender, CancellationToken ct) =>
        Results.Ok(await sender.Send(new GetRouteListQuery(), ct));

    private static async Task<IResult> GetRouteById(ISender sender, Guid id, CancellationToken ct) =>
        Results.Ok(await sender.Send(new GetRouteDetailQuery(id), ct));

    private static async Task<IResult> CreateRoute(ISender sender, CreateRouteCommand command, CancellationToken ct) =>
        Results.Ok(await sender.Send(command, ct));

    private static async Task<IResult> UpdateRoute(ISender sender, Guid id, UpdateRouteRequest req, CancellationToken ct) =>
        Results.Ok(await sender.Send(new UpdateRouteCommand(
            id, req.RouteName, req.Description, req.BaseDistanceKm, req.EstimatedDurationMin, req.Status), ct));

    private static async Task<IResult> AddRouteStop(ISender sender, Guid id, AddRouteStopRequest req, CancellationToken ct) =>
        Results.Ok(await sender.Send(new AddRouteStopCommand(
            id, req.StationId, req.StopOrder, req.StandardTravelMin,
            req.StandardDwellMin, req.IsPickupAllowed, req.IsDropoffAllowed), ct));

    private static async Task<IResult> UpdateRouteStop(ISender sender, Guid id, Guid stopId, UpdateRouteStopRequest req, CancellationToken ct) =>
        Results.Ok(await sender.Send(new UpdateRouteStopCommand(
            id, stopId, req.StandardTravelMin, req.StandardDwellMin,
            req.IsPickupAllowed, req.IsDropoffAllowed), ct));

    private static async Task<IResult> RemoveRouteStop(ISender sender, Guid id, Guid stopId, CancellationToken ct)
    {
        await sender.Send(new RemoveRouteStopCommand(id, stopId), ct);
        return Results.NoContent();
    }

    public sealed record UpdateRouteRequest(string RouteName, string? Description, decimal? BaseDistanceKm, int? EstimatedDurationMin, string Status);
    public sealed record AddRouteStopRequest(Guid StationId, int StopOrder, int? StandardTravelMin, int? StandardDwellMin, bool IsPickupAllowed, bool IsDropoffAllowed);
    public sealed record UpdateRouteStopRequest(int? StandardTravelMin, int? StandardDwellMin, bool IsPickupAllowed, bool IsDropoffAllowed);
}
