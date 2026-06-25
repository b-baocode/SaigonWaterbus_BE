using SaigonWaterbus.Application.TicketTypes;

namespace SaigonWaterbus.Web.Endpoints;

public sealed class TicketTypes : IEndpointGroup
{
    public static string RoutePrefix => "/api/ticket-types";

    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet(GetTicketTypes, string.Empty)
            .AllowAnonymous()
            .WithSummary("Danh sach loai ve")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Anonymous",
                null,
                "Tra ve danh sach loai ve co dinh trong code, khong doc tu bang database.",
                "priceModifier: he so gia (1.0 = gia goc, 0.5 = giam 50%).",
                "Ticket type thuc te cua ve da phat hanh duoc luu truc tiep trong bang tickets."));
    }

    private static async Task<IResult> GetTicketTypes(ISender sender, CancellationToken ct) =>
        Results.Ok(await sender.Send(new GetTicketTypeListQuery(), ct));
}
