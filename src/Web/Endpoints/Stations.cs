using SaigonWaterbus.Application.Stations;
using SaigonWaterbus.Web.Infrastructure;

namespace SaigonWaterbus.Web.Endpoints;

public sealed class Stations : IEndpointGroup
{
    public static string RoutePrefix => "/api/stations";

    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet(GetStations, string.Empty).AllowAnonymous();
        group.MapGet(GetStationById, "{id:guid}").AllowAnonymous();
        group.MapPost(CreateStation, string.Empty).RequireAuthorization();
        group.MapPut(UpdateStation, "{id:guid}").RequireAuthorization();
        group.MapDelete(DeleteStation, "{id:guid}").RequireAuthorization();
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
