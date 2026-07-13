using Microsoft.AspNetCore.Http;
using SaigonWaterbus.Application.Routes;

namespace SaigonWaterbus.Web.Endpoints;

public sealed class Routes : IEndpointGroup
{
    public static string RoutePrefix => "/api/routes";

    private const string CreateRouteFromRoutesExample =
        """
        {
          "routeCode": "R01-A-B-C",
          "routeName": "Tuyen A - B - C",
          "routeType": "Regular",
          "description": "Ghep tu cac route GPS da thu.",
          "sourceRouteIds": [
            "550e8400-e29b-41d4-a716-446655440001",
            "550e8400-e29b-41d4-a716-446655440002"
          ]
        }
        """;

    private const string UpdateRouteExample =
        """
        {
          "routeName": "Tuyen 02: Bach Dang → Nguyen Van Linh (cap nhat)",
          "routeType": "Regular",
          "description": "Mo ta moi",
          "baseDistanceKm": 16.0,
          "estimatedDurationMin": 80,
          "status": "Active"
        }
        """;

    private const string UpdateStopExample =
        """
        {
          "standardTravelMin": 10,
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

        group.MapPost(CreateRouteFromRoutes, "from-routes")
            .RequireAuthorization()
            .WithSummary("Tao route moi bang cach ghep cac route co san")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin",
                CreateRouteFromRoutesExample,
                "sourceRouteIds phai gui dung thu tu luong di.",
                "Ben cuoi cua route truoc phai trung ben dau cua route sau.",
                "BE tao route moi doc lap trong routes va tao stops[] day du trong route_stops.",
                "Route nguon van giu nguyen, co the tiep tuc dung cho charter/tham chieu.",
                "Regular: ben dau/cuoi khac nhau. SightseeingLoop: ben dau/cuoi trung nhau. CharterReference: linh hoat."));

        group.MapPut(UpdateRoute, "{id:guid}")
            .RequireAuthorization()
            .WithSummary("Cap nhat thong tin tuyen")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin",
                UpdateRouteExample,
                "Status hop le: Active | Inactive.",
                "routeType: Regular | SightseeingLoop | CharterReference.",
                "BE tu xac dinh kha nang dung cho dat ve theo routeType; FE khong can hien thi lua chon nay.",
                "RouteCode khong doi duoc sau khi tao."));

        group.MapPut(UpdateRouteStop, "{id:guid}/stops/{stopId:guid}")
            .RequireAuthorization()
            .WithSummary("Cap nhat ben dung")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin",
                UpdateStopExample,
                "Khong doi duoc stationId hay stopOrder sau khi tao.",
                "Dung API nay de chinh thoi gian di chuyen va quyen len/xuong."));

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
                "He thong se cap nhat Station va thay moi cac WaterwaySegment trung OsmId de su dung cho GPS/ban do ve sau."));
    }

    private static async Task<IResult> GetRoutes(ISender sender, CancellationToken ct) =>
        Results.Ok(await sender.Send(new GetRouteListQuery(), ct));

    private static async Task<IResult> GetRouteById(ISender sender, Guid id, CancellationToken ct) =>
        Results.Ok(await sender.Send(new GetRouteDetailQuery(id), ct));

    private static async Task<IResult> CreateRouteFromRoutes(ISender sender, CreateRouteFromRoutesCommand command, CancellationToken ct) =>
        Results.Ok(await sender.Send(command, ct));

    private static async Task<IResult> UpdateRoute(ISender sender, Guid id, UpdateRouteRequest req, CancellationToken ct) =>
        Results.Ok(await sender.Send(new UpdateRouteCommand(
            id, req.RouteName, req.RouteType, req.Description, req.BaseDistanceKm, req.EstimatedDurationMin, req.Status), ct));

    private static async Task<IResult> ImportGeoJson(
        ISender sender,
        IFormFile file,
        CancellationToken ct)
    {
        using var reader = new System.IO.StreamReader(file.OpenReadStream(), System.Text.Encoding.UTF8);
        var geoJsonContent = await reader.ReadToEndAsync(ct);
        return Results.Ok(await sender.Send(new ImportRouteGeoJsonCommand(geoJsonContent), ct));
    }

    private static async Task<IResult> UpdateRouteStop(ISender sender, Guid id, Guid stopId, UpdateRouteStopRequest req, CancellationToken ct) =>
        Results.Ok(await sender.Send(new UpdateRouteStopCommand(
            id, stopId, req.StandardTravelMin,
            req.IsPickupAllowed, req.IsDropoffAllowed), ct));

    private static async Task<IResult> DeleteRoute(ISender sender, Guid id, CancellationToken ct)
    {
        await sender.Send(new DeleteRouteCommand(id), ct);
        return Results.NoContent();
    }

    public sealed record UpdateRouteRequest(string RouteName, string? RouteType, string? Description, decimal? BaseDistanceKm, int? EstimatedDurationMin, string Status);
    public sealed record UpdateRouteStopRequest(int? StandardTravelMin, bool IsPickupAllowed, bool IsDropoffAllowed);

}
