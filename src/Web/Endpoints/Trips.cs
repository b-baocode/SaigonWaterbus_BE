using SaigonWaterbus.Application.Trips;
using SaigonWaterbus.Domain.Enums;
using SaigonWaterbus.Web.Infrastructure;

namespace SaigonWaterbus.Web.Endpoints;

public sealed class Trips : IEndpointGroup
{
    public static string RoutePrefix => "/api/trips";

    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet(SearchTrips, "search").AllowAnonymous();
        group.MapGet(GetTripById, "{id:guid}").AllowAnonymous();
        group.MapGet(GetTripSeats, "{id:guid}/seats").AllowAnonymous();
        group.MapPost(CreateTrip, string.Empty).RequireAuthorization();
        group.MapPatch(UpdateTripStatus, "{id:guid}/status").RequireAuthorization();
    }

    private static async Task<IResult> SearchTrips(
        ISender sender,
        Guid fromStationId, Guid toStationId, DateOnly operatingDate,
        CancellationToken ct) =>
        Results.Ok(await sender.Send(new SearchTripsQuery(fromStationId, toStationId, operatingDate), ct));

    private static async Task<IResult> GetTripById(ISender sender, Guid id, CancellationToken ct) =>
        Results.Ok(await sender.Send(new GetTripDetailQuery(id), ct));

    private static async Task<IResult> GetTripSeats(ISender sender, Guid id, CancellationToken ct) =>
        Results.Ok(await sender.Send(new GetTripAvailableSeatsQuery(id), ct));

    private static async Task<IResult> CreateTrip(ISender sender, CreateTripCommand command, CancellationToken ct) =>
        Results.Ok(await sender.Send(command, ct));

    private static async Task<IResult> UpdateTripStatus(ISender sender, Guid id, UpdateTripStatusRequest req, CancellationToken ct) =>
        Results.Ok(await sender.Send(new UpdateTripStatusCommand(id, req.TripStatus, req.StatusNote), ct));

    public sealed record UpdateTripStatusRequest(TripStatus TripStatus, string? StatusNote);
}
