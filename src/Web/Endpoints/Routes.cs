using Microsoft.AspNetCore.Http;
using SaigonWaterbus.Application.Routes;

namespace SaigonWaterbus.Web.Endpoints;

public sealed class Routes : IEndpointGroup
{
    public static string RoutePrefix => "/api/routes";


    private const string CreateRouteExample =
        """
        {
          "routeCode": "R01-BD-LD2",
          "routeName": "Tuyen 01: Bach Dang - Linh Dong",
          "boatId": null,
          "waypoints": [
            { "type": "station",     "stationCode": "ST-BD",          "stopOrder": 1 },
            { "type": "viaWaterway", "waterwayOsmId": "way/708678320" },
            { "type": "station",     "stationCode": "ST-TT",          "stopOrder": 2 },
            { "type": "station",     "stationCode": "ST-BS",          "stopOrder": 3 },
            { "type": "station",     "stationCode": "ST-BA",          "stopOrder": 4 },
            { "type": "viaWaterway", "waterwayOsmId": "way/552372203" },
            { "type": "station",     "stationCode": "ST-TD2",         "stopOrder": 5 },
            { "type": "station",     "stationCode": "ST-TD",          "stopOrder": 6 },
            { "type": "station",     "stationCode": "ST-HBC",         "stopOrder": 7 },
            { "type": "station",     "stationCode": "ST-LD",          "stopOrder": 8 }
          ]
        }
        """;

    private const string PreviewGeometryExample =
        """
        {
          "waypoints": [
            { "type": "station", "stationCode": "ST-BD", "stopOrder": 1 },
            { "type": "station", "stationCode": "ST-LD", "stopOrder": 2 }
          ],
          "maxAlternatives": 3
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
                "Admin",
                CreateRouteExample,
                "RouteCode phai unique (tu dong uppercase).",
                "Request phai chua it nhat 2 waypoint type=station.",
                "Waypoint dau va cuoi bat buoc la station.",
                "CHI CAN NHAP STATION: he thong TU DONG tao RouteGeometry tu mang waterway da import (snap ben vao duong song, tim duong ngan nhat). Neu chua import mang hoac khong noi duoc thi van tao route voi geometry rong.",
                "autoRouteGeometry: true = BAT BUOC tao geometry (tra 400 neu khong the); false = khong tao; bo trong = tu dong best-effort.",
                "chosenGeometry (optional): mang [[lon,lat],...] lay tu 1 phuong an cua POST /api/routes/geometry-preview - dung nguyen geometry nay thay vi tu tinh (khong dung kem viaWaterway).",
                "Neu co waypoint type=viaWaterway thi geometry la bat buoc va tuyen bi ep di qua waterway do.",
                "viaWaterway = EP di qua mot con duong thuy (vd duong tat); Dijkstra buoc phai qua diem dai dien cua no.",
                "avoidWaterwayOsmIds (optional): mang OSM id/ten waterway EP NE - loai khoi mang khi tim duong, de tuyen di duong dai hon (vd vong theo song thay vi cat qua kenh). Khong duoc trung voi viaWaterway.",
                "estimatedDurationMin KHONG con nhap tay - he thong tu tinh = quang duong (BaseDistanceKm) / (MaxSpeedKmh * 70%).",
                "boatId (optional): thuyen dung de uoc tinh thoi gian. Bo trong -> EstimatedDurationMin = null. Boat khong ton tai / khong co MaxSpeedKmh -> bao loi 400. Khong dung duoc geometry (khong co quang duong) -> EstimatedDurationMin = null.",
                "Neu ban ve duong san, co the tao route chi bang station waypoints roi nhap duong that o /segments."));

        group.MapPost(PreviewRouteGeometry, "geometry-preview")
            .RequireAuthorization()
            .WithSummary("Xem truoc cac phuong an duong di (khong luu)")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin",
                PreviewGeometryExample,
                "Nhan waypoints giong POST /api/routes, tra ve toi da maxAlternatives (mac dinh 3) phuong an duong di.",
                "Phuong an 1 la duong ngan nhat; cac phuong an sau di duong khac (vd vong theo song thay vi cat rach).",
                "Moi phuong an: { option, distanceKm, geometry: [[lon,lat],...] }.",
                "FE ve cac phuong an len ban do cho admin chon, roi goi POST /api/routes kem chosenGeometry = geometry cua phuong an da chon.",
                "Ho tro preferWaterwayType va avoidWaterwayOsmIds nhu khi tao route."));

        group.MapPut(UpdateRoute, "{id:guid}")
            .RequireAuthorization()
            .WithSummary("Cap nhat thong tin tuyen")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin",
                UpdateRouteExample,
                "Status hop le: Active | Inactive.",
                "RouteCode khong doi duoc sau khi tao."));

        // DISABLED 2026-07-12: tam khoa API them ben dung vao tuyen (khong xoa code, bo comment de bat lai).
        // group.MapPost(AddRouteStop, "{id:guid}/stops")
        //     .RequireAuthorization()
        //     .WithSummary("Them ben dung vao tuyen")
        //     .WithDescription(OpenApiDescriptionBuilder.Build(
        //         "Admin",
        //         AddStopExample,
        //         "stopOrder phai unique trong cung mot tuyen.",
        //         "stop cuoi (isDropoffAllowed=true, isPickupAllowed=false).",
        //         "stop dau (isPickupAllowed=true, isDropoffAllowed=false).",
        //         "standardTravelMin: phut di tu stop nay den stop tiep theo.",
        //         "standardDwellMin: phut tau dung tai stop (default 2)."));

        group.MapPut(UpdateRouteStop, "{id:guid}/stops/{stopId:guid}")
            .RequireAuthorization()
            .WithSummary("Cap nhat ben dung")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin",
                UpdateStopExample,
                "Khong doi duoc stationId hay stopOrder sau khi tao.",
                "Dung API nay de chinh thoi gian di chuyen va quyen len/xuong."));

        group.MapDelete(RemoveRouteStop, "{id:guid}/stops/{stopId:guid}")
            .RequireAuthorization()
            .WithSummary("Xoa ben dung khoi tuyen")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin",
                null,
                "Tra ve 204 khi xoa thanh cong.",
                "Tra ve 404 neu khong tim thay tuyen hoac stop."));

        group.MapDelete(DeleteRoute, "{id:guid}")
            .RequireAuthorization()
            .WithSummary("Xoa tuyen")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin",
                null,
                "Chi xoa duoc tuyen chua co trip nao.",
                "Neu tuyen da co trip, dung PUT /{id} de doi Status=Inactive thay vi xoa.",
                "Tra ve 204 khi xoa thanh cong."));

        group.MapPost(ImportGeoJson, "geojson-import")
            .RequireAuthorization()
            .DisableAntiforgery()
            .WithSummary("Import mang song rach va ben tu file GeoJSON")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin",
                null,
                "Content-Type: multipart/form-data.",
                "Form fields: file (.geojson file).",
                "Nhan LineString/MultiLineString de import duong song/duong nen; neu co waterway=river|canal thi giu loai do, neu khong thi luu waterway_type=custom.",
                "Point co amenity=ferry_terminal se duoc dung de tao/cap nhat station neu file co chua.",
                "Neu line co from_station_code, to_station_code va waterbus_route/route_code thi he thong se tao/cap nhat route va route stops tu dong.",
                "He thong se cap nhat Station va thay moi cac WaterwaySegment trung OsmId de API tao route su dung ve sau.",
                "API tao route co the dung viaWaterway neu muon tao route geometry tu mang duong nen da import."));
    }

    private static async Task<IResult> GetRoutes(ISender sender, CancellationToken ct) =>
        Results.Ok(await sender.Send(new GetRouteListQuery(), ct));

    private static async Task<IResult> GetRouteById(ISender sender, Guid id, CancellationToken ct) =>
        Results.Ok(await sender.Send(new GetRouteDetailQuery(id), ct));

    private static async Task<IResult> CreateRoute(ISender sender, CreateRouteCommand command, CancellationToken ct) =>
        Results.Ok(await sender.Send(command, ct));

    private static async Task<IResult> PreviewRouteGeometry(
        ISender sender, PreviewRouteGeometryCommand command, CancellationToken ct) =>
        Results.Ok(await sender.Send(command, ct));

    private static async Task<IResult> UpdateRoute(ISender sender, Guid id, UpdateRouteRequest req, CancellationToken ct) =>
        Results.Ok(await sender.Send(new UpdateRouteCommand(
            id, req.RouteName, req.Description, req.BaseDistanceKm, req.EstimatedDurationMin, req.Status), ct));

    private static async Task<IResult> ImportGeoJson(
        ISender sender,
        IFormFile file,
        CancellationToken ct)
    {
        using var reader = new System.IO.StreamReader(file.OpenReadStream(), System.Text.Encoding.UTF8);
        var geoJsonContent = await reader.ReadToEndAsync(ct);
        return Results.Ok(await sender.Send(new ImportRouteGeoJsonCommand(geoJsonContent), ct));
    }

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

    private static async Task<IResult> DeleteRoute(ISender sender, Guid id, CancellationToken ct)
    {
        await sender.Send(new DeleteRouteCommand(id), ct);
        return Results.NoContent();
    }

    public sealed record UpdateRouteRequest(string RouteName, string? Description, decimal? BaseDistanceKm, int? EstimatedDurationMin, string Status);
    public sealed record AddRouteStopRequest(string StationCode, int StopOrder, int? StandardTravelMin, int? StandardDwellMin, bool IsPickupAllowed, bool IsDropoffAllowed);
    public sealed record UpdateRouteStopRequest(int? StandardTravelMin, int? StandardDwellMin, bool IsPickupAllowed, bool IsDropoffAllowed);

}
