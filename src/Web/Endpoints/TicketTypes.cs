using SaigonWaterbus.Application.TicketTypes;
using SaigonWaterbus.Web.Infrastructure;

namespace SaigonWaterbus.Web.Endpoints;

public sealed class TicketTypes : IEndpointGroup
{
    public static string RoutePrefix => "/api/ticket-types";

    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet(GetTicketTypes, string.Empty).AllowAnonymous();
        group.MapPost(CreateTicketType, string.Empty).RequireAuthorization();
        group.MapPut(UpdateTicketType, "{id:guid}").RequireAuthorization();
    }

    private static async Task<IResult> GetTicketTypes(ISender sender, CancellationToken ct) =>
        Results.Ok(await sender.Send(new GetTicketTypeListQuery(), ct));

    private static async Task<IResult> CreateTicketType(ISender sender, CreateTicketTypeCommand command, CancellationToken ct) =>
        Results.Ok(await sender.Send(command, ct));

    private static async Task<IResult> UpdateTicketType(ISender sender, Guid id, UpdateTicketTypeRequest req, CancellationToken ct) =>
        Results.Ok(await sender.Send(new UpdateTicketTypeCommand(
            id, req.TicketTypeName, req.Description, req.PriceModifier, req.PointsEarnedRate, req.IsActive), ct));

    public sealed record UpdateTicketTypeRequest(
        string TicketTypeName, string? Description,
        decimal PriceModifier, int PointsEarnedRate, bool IsActive);
}
