using System.IO;
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
          "estimatedDurationMin": 60,
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

    private const string UpdateGeoJsonExample =
        """
        multipart/form-data
        file: route.geojson
        updateStations: true
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

        group.MapPut(UpdateRouteGeoJson, "{id:guid}/geojson")
            .RequireAuthorization()
            .DisableAntiforgery()
            .Accepts<UpdateRouteGeoJsonFormRequest>("multipart/form-data")
            .WithSummary("Cap nhat file GeoJSON cho route")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Bearer token",
                UpdateGeoJsonExample,
                "Gui multipart/form-data voi field file (.geojson).",
                "LineString/MultiLineString duoc dung de cap nhat RouteGeometry va BaseDistanceKm.",
                "Neu file co nhieu line, nen gan properties waterbus_route hoac route_code khop RouteCode.",
                "Point co station_code hoac amenity=ferry_terminal se cap nhat latitude/longitude cho station trong route.",
                "Co the gui updateStations=false neu khong muon cap nhat toa do station."));

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
            .Accepts<ImportRouteGeoJsonFormRequest>("multipart/form-data")
            .WithSummary("Import/cap nhat route va station tu file GeoJSON")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Bearer token",
                UpdateGeoJsonExample,
                "Gui multipart/form-data voi field file (.geojson).",
                "Point co station_code/public_transport/amenity=ferry_terminal se tao hoac cap nhat station.",
                "LineString/MultiLineString co from_station_code va to_station_code se tao hoac cap nhat route stops.",
                "Co the dung waterbus_route hoac route_code tren line feature de chi dinh RouteCode."));
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

    private static async Task<IResult> UpdateRouteGeoJson(
        ISender sender,
        Guid id,
        HttpRequest request,
        CancellationToken ct)
    {
        if (!request.HasFormContentType)
        {
            return Results.BadRequest(new { message = "Gui multipart/form-data voi field file." });
        }

        var form = await request.ReadFormAsync(ct);
        var file = form.Files.GetFile("file") ?? form.Files.FirstOrDefault();
        if (file is null || file.Length == 0)
        {
            return Results.BadRequest(new { message = "File GeoJSON khong duoc de trong." });
        }

        await using var fileStream = file.OpenReadStream();
        using var reader = new StreamReader(fileStream, System.Text.Encoding.UTF8);
        var geoJsonContent = await reader.ReadToEndAsync(ct);
        var updateStations = ParseOptionalBool(form["updateStations"].ToString()) ?? true;

        return Results.Ok(await sender.Send(new UpdateRouteGeoJsonCommand(id, geoJsonContent, updateStations), ct));
    }

    private static async Task<IResult> ImportGeoJson(
        ISender sender,
        HttpRequest request,
        CancellationToken ct)
    {
        if (!request.HasFormContentType)
        {
            return Results.BadRequest(new { message = "Gui multipart/form-data voi field file." });
        }

        var form = await request.ReadFormAsync(ct);
        var file = form.Files.GetFile("file") ?? form.Files.FirstOrDefault();
        if (file is null || file.Length == 0)
        {
            return Results.BadRequest(new { message = "File GeoJSON khong duoc de trong." });
        }

        await using var fileStream = file.OpenReadStream();
        using var reader = new StreamReader(fileStream, System.Text.Encoding.UTF8);
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

    private sealed record ImportRouteGeoJsonFormRequest(IFormFile File);

    private sealed record UpdateRouteGeoJsonFormRequest(IFormFile File, bool? UpdateStations = null);

    private static bool? ParseOptionalBool(string? value) =>
        bool.TryParse(value, out var result) ? result : null;
}
