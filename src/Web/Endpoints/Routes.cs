using Microsoft.AspNetCore.Http;
using SaigonWaterbus.Application.Routes;

namespace SaigonWaterbus.Web.Endpoints;

public sealed class Routes : IEndpointGroup
{
    public static string RoutePrefix => "/api/routes";


    private const string CreateRouteExample =
        """
        {
          "routeCode": "R02-BD-NVL",
          "routeName": "Tuyen 02: Bach Dang den Linh Dong",
          "description": "Tuyen waterbus Q1 → Q7",
          "estimatedDurationMin": 75,
          "waypoints": [
            { "type": "station", "stationCode": "BD" },
            { "type": "viaWaterway", "waterwayOsmId": "way/123456" },
            { "type": "station", "stationCode": "LD" }
          ]
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
          "stationCode": "BD",
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

    private const string SegmentExample =
        """
        {
          "fromStationCode": "BD",
          "toStationCode": "BS",
          "segmentOrder": 1,
          "distanceKm": 1.25,
          "geometry": [
            { "longitude": 106.7061, "latitude": 10.7731 },
            { "longitude": 106.7132, "latitude": 10.7810 }
          ]
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
                "Request phai chua it nhat 2 waypoint type=station.",
                "Waypoint dau va cuoi bat buoc la station.",
                "Neu co waypoint type=viaWaterway thi he thong dung mang waterway da import truoc do de tao RouteGeometry LineString.",
                "Neu ban ve duong san, co the tao route chi bang station waypoints roi nhap duong that o /segments."));

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

        group.MapPost(AddRouteSegment, "{id:guid}/segments")
            .RequireAuthorization()
            .WithSummary("Them doan chay giua 2 ben trong tuyen")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Bearer token",
                SegmentExample,
                "Dung de khai bao khoang cach va thoi gian chay that cho custom booking.",
                "fromStationCode va toStationCode phai nam trong stops cua route.",
                "geometry la danh sach diem [longitude, latitude] cua doan duong thuy, co the bo trong neu chua ve xong."));

        group.MapPut(UpdateRouteSegment, "{id:guid}/segments/{segmentId:guid}")
            .RequireAuthorization()
            .WithSummary("Cap nhat doan chay giua 2 ben trong tuyen")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Bearer token",
                SegmentExample,
                "Dung de cap nhat khoang cach, thoi gian chay, thu tu va geometry cua doan.",
                "Neu truyen geometry thi phai co it nhat 2 diem."));

        group.MapDelete(RemoveRouteSegment, "{id:guid}/segments/{segmentId:guid}")
            .RequireAuthorization()
            .WithSummary("Xoa doan chay khoi tuyen")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Bearer token",
                null,
                "Tra ve 204 khi xoa thanh cong.",
                "Tra ve 404 neu khong tim thay route segment."));

        group.MapDelete(DeleteRoute, "{id:guid}")
            .RequireAuthorization()
            .WithSummary("Xoa tuyen")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Bearer token",
                null,
                "Chi xoa duoc tuyen chua co trip nao.",
                "Neu tuyen da co trip, dung PUT /{id} de doi Status=Inactive thay vi xoa.",
                "Tra ve 204 khi xoa thanh cong."));

        group.MapPost(ImportGeoJson, "geojson-import")
            .RequireAuthorization()
            .DisableAntiforgery()
            .WithSummary("Import mang song rach va ben tu file GeoJSON")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Bearer token",
                null,
                "Content-Type: multipart/form-data.",
                "Form fields: file (.geojson file).",
                "Nhan LineString/MultiLineString de import duong song/duong nen; neu co waterway=river|canal thi giu loai do, neu khong thi luu waterway_type=custom.",
                "Point co amenity=ferry_terminal se duoc dung de tao/cap nhat station neu file co chua.",
                "Neu line co from_station_code, to_station_code va waterbus_route/route_code thi he thong se tao/cap nhat route, route stops va route segments tu dong.",
                "He thong se cap nhat Station va thay moi toan bo mang WaterwaySegment de API tao route su dung ve sau.",
                "API tao route co the dung viaWaterway neu muon tao route geometry tu mang duong nen da import."));
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
            id, req.StationCode, req.StopOrder, req.StandardTravelMin,
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

    private static async Task<IResult> AddRouteSegment(ISender sender, Guid id, RouteSegmentRequest req, CancellationToken ct) =>
        Results.Ok(await sender.Send(new AddRouteSegmentCommand(
            id, req.FromStationCode, req.ToStationCode, req.SegmentOrder,
            req.DistanceKm, req.EstimatedTravelMinutes, req.Geometry), ct));

    private static async Task<IResult> UpdateRouteSegment(ISender sender, Guid id, Guid segmentId, RouteSegmentRequest req, CancellationToken ct) =>
        Results.Ok(await sender.Send(new UpdateRouteSegmentCommand(
            id, segmentId, req.FromStationCode, req.ToStationCode, req.SegmentOrder,
            req.DistanceKm, req.EstimatedTravelMinutes, req.Geometry), ct));

    private static async Task<IResult> RemoveRouteSegment(ISender sender, Guid id, Guid segmentId, CancellationToken ct)
    {
        await sender.Send(new RemoveRouteSegmentCommand(id, segmentId), ct);
        return Results.NoContent();
    }

    private static async Task<IResult> DeleteRoute(ISender sender, Guid id, CancellationToken ct)
    {
        await sender.Send(new DeleteRouteCommand(id), ct);
        return Results.NoContent();
    }

    private static async Task<IResult> ImportGeoJson(
        ISender sender,
        IFormFile file,
        CancellationToken ct)
    {
        using var reader = new System.IO.StreamReader(file.OpenReadStream(), System.Text.Encoding.UTF8);
        var geoJsonContent = await reader.ReadToEndAsync(ct);
        return Results.Ok(await sender.Send(new ImportRouteGeoJsonCommand(geoJsonContent), ct));
    }

    public sealed record UpdateRouteRequest(string RouteName, string? Description, decimal? BaseDistanceKm, int? EstimatedDurationMin, string Status);
    public sealed record AddRouteStopRequest(string StationCode, int StopOrder, int? StandardTravelMin, int? StandardDwellMin, bool IsPickupAllowed, bool IsDropoffAllowed);
    public sealed record UpdateRouteStopRequest(int? StandardTravelMin, int? StandardDwellMin, bool IsPickupAllowed, bool IsDropoffAllowed);
    public sealed record RouteSegmentRequest(
        string FromStationCode,
        string ToStationCode,
        int SegmentOrder,
        decimal DistanceKm,
        int? EstimatedTravelMinutes = null,
        IReadOnlyList<RouteSegmentCoordinateDto>? Geometry = null);
}
