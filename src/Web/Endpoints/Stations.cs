using SaigonWaterbus.Application.Stations;

namespace SaigonWaterbus.Web.Endpoints;

public sealed class Stations : IEndpointGroup
{
    public static string RoutePrefix => "/api/stations";

    private const string CreateExample =
        """
        {
          "stationCode": "NVL",
          "stationName": "Ben Nguyen Van Linh",
          "address": "Q7, TP.HCM",
          "latitude": 10.7285,
          "longitude": 106.7006
        }
        """;

    private const string UpdateExample =
        """
        {
          "stationName": "Ben Nguyen Van Linh (cap nhat)",
          "address": "Q7, TP.HCM",
          "latitude": 10.7285,
          "longitude": 106.7006,
          "status": "Active"
        }
        """;

    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet(GetStations, string.Empty)
            .AllowAnonymous()
            .WithSummary("Danh sach tram")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Anonymous",
                null,
                "Tra ve tat ca tram co status = Active.",
                "Sap xep theo StationName."));

        group.MapGet(GetStationById, "{id:guid}")
            .AllowAnonymous()
            .WithSummary("Chi tiet tram")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Anonymous",
                null,
                "Tra ve 404 neu khong tim thay tram."));

        group.MapPost(CreateStation, string.Empty)
            .RequireAuthorization()
            .WithSummary("Tao tram moi")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Bearer token",
                CreateExample,
                "StationCode phai unique (tu dong uppercase).",
                "Status mac dinh la Active khi tao moi."));

        group.MapPut(UpdateStation, "{id:guid}")
            .RequireAuthorization()
            .WithSummary("Cap nhat tram")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Bearer token",
                UpdateExample,
                "Status hop le: Active | Inactive.",
                "StationCode khong doi duoc sau khi tao."));

        group.MapDelete(DeleteStation, "{id:guid}")
            .RequireAuthorization()
            .WithSummary("Xoa tram")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Bearer token",
                null,
                "Tra ve 204 khi xoa thanh cong.",
                "Tra ve 404 neu khong tim thay tram."));
    }

    private static async Task<IResult> GetStations(ISender sender, CancellationToken ct) =>
        Results.Ok(await sender.Send(new GetStationListQuery(), ct));

    private static async Task<IResult> GetStationById(ISender sender, Guid id, CancellationToken ct) =>
        Results.Ok(await sender.Send(new GetStationDetailQuery(id), ct));

    private static async Task<IResult> CreateStation(ISender sender, CreateStationCommand command, CancellationToken ct) =>
        Results.Ok(await sender.Send(command, ct));

    private static async Task<IResult> UpdateStation(ISender sender, Guid id, UpdateStationRequest req, CancellationToken ct) =>
        Results.Ok(await sender.Send(new UpdateStationCommand(
            id, req.StationName, req.Address, req.Latitude, req.Longitude, req.Status), ct));

    private static async Task<IResult> DeleteStation(ISender sender, Guid id, CancellationToken ct)
    {
        await sender.Send(new DeleteStationCommand(id), ct);
        return Results.NoContent();
    }

    public sealed record UpdateStationRequest(
        string StationName,
        string? Address,
        decimal? Latitude,
        decimal? Longitude,
        string Status);
}
