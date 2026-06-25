using SaigonWaterbus.Application.Tickets;

namespace SaigonWaterbus.Web.Endpoints;

public sealed class Tickets : IEndpointGroup
{
    public static string RoutePrefix => "/api/tickets";

    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet(ScanTicket, "scan/{codeOrToken}")
            .RequireAuthorization()
            .WithSummary("Quet/tra cuu ma ve")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Bearer token",
                null,
                "Nhan ticketCode hoac qrToken.",
                "Admin/Manager/Staff xem duoc moi ve.",
                "Customer chi xem duoc ve thuoc booking cua minh.",
                "Custom booking chi sinh 1 ve cap booking sau khi da thanh toan du va nhap danh sach hanh khach."));
    }

    private static async Task<IResult> ScanTicket(
        ISender sender,
        string codeOrToken,
        CancellationToken ct) =>
        Results.Ok(await sender.Send(new ScanTicketQuery(codeOrToken), ct));
}
