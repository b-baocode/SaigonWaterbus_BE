using SaigonWaterbus.Application.Routes;
using SaigonWaterbus.Web.Infrastructure;

namespace SaigonWaterbus.Web.Endpoints;

public sealed class Routes : IEndpointGroup
{
    public static string RoutePrefix => "/api/routes";

    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet(GetRoutes, string.Empty).AllowAnonymous();
        group.MapGet(GetRouteById, "{id:guid}").AllowAnonymous();
        group.MapPost(CreateRoute, string.Empty).RequireAuthorization();
        group.MapPut(UpdateRoute, "{id:guid}").RequireAuthorization();
        group.MapPost(AddRouteStop, "{id:guid}/stops").RequireAuthorization();
        group.MapPut(UpdateRouteStop, "{id:guid}/stops/{stopId:guid}").RequireAuthorization();
        group.MapDelete(RemoveRouteStop, "{id:guid}/stops/{stopId:guid}").RequireAuthorization();
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
