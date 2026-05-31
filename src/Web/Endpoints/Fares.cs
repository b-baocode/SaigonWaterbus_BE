using SaigonWaterbus.Application.Fares;
using SaigonWaterbus.Web.Infrastructure;

namespace SaigonWaterbus.Web.Endpoints;

public sealed class Fares : IEndpointGroup
{
    public static string RoutePrefix => "/api/fares";

    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet(GetFare, string.Empty).AllowAnonymous();
        group.MapGet(GetFareMatrix, "matrix").RequireAuthorization();
        group.MapPost(CreateFare, string.Empty).RequireAuthorization();
        group.MapPut(UpdateFare, "{id:guid}").RequireAuthorization();
    }

    private static async Task<IResult> GetFare(
        ISender sender,
        Guid routeId, Guid fromStationId, Guid toStationId,
        CancellationToken ct) =>
        Results.Ok(await sender.Send(new GetFareQuery(routeId, fromStationId, toStationId), ct));

    private static async Task<IResult> GetFareMatrix(ISender sender, Guid? routeId, CancellationToken ct) =>
        Results.Ok(await sender.Send(new GetFareMatrixListQuery(routeId), ct));

    private static async Task<IResult> CreateFare(ISender sender, CreateFareCommand command, CancellationToken ct) =>
        Results.Ok(await sender.Send(command, ct));

    private static async Task<IResult> UpdateFare(ISender sender, Guid id, UpdateFareRequest req, CancellationToken ct) =>
        Results.Ok(await sender.Send(new UpdateFareCommand(id, req.BasePrice, req.IsActive), ct));

    public sealed record UpdateFareRequest(decimal BasePrice, bool IsActive);
}
