using SaigonWaterbus.Application.Boats;
using SaigonWaterbus.Web.Infrastructure;

namespace SaigonWaterbus.Web.Endpoints;

public sealed class Boats : IEndpointGroup
{
    public static string RoutePrefix => "/api/boats";

    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet(GetBoats, string.Empty).RequireAuthorization();
        group.MapPost(CreateBoat, string.Empty).RequireAuthorization();
        group.MapPut(UpdateBoat, "{id:guid}").RequireAuthorization();
        group.MapPost(CreateBoatSeats, "{id:guid}/seats/batch").RequireAuthorization();
    }

    private static async Task<IResult> GetBoats(ISender sender, CancellationToken ct) =>
        Results.Ok(await sender.Send(new GetBoatListQuery(), ct));

    private static async Task<IResult> CreateBoat(ISender sender, CreateBoatCommand command, CancellationToken ct) =>
        Results.Ok(await sender.Send(command, ct));

    private static async Task<IResult> UpdateBoat(ISender sender, Guid id, UpdateBoatRequest req, CancellationToken ct) =>
        Results.Ok(await sender.Send(new UpdateBoatCommand(id, req.BoatName, req.Capacity, req.BoatStatus, req.Description), ct));

    private static async Task<IResult> CreateBoatSeats(ISender sender, Guid id, CreateSeatsRequest req, CancellationToken ct) =>
        Results.Ok(await sender.Send(new CreateSeatsCommand(id, req.Rows, req.Columns, req.SeatClass), ct));

    public sealed record UpdateBoatRequest(string BoatName, int Capacity, string BoatStatus, string? Description);
    public sealed record CreateSeatsRequest(int Rows, int Columns, string? SeatClass);
}
