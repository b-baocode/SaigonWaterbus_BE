using SaigonWaterbus.Application.Tickets;
using QRCoder;

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

        group.MapPost(CheckInTicket, "check-in/{codeOrToken}")
            .RequireAuthorization()
            .WithSummary("Check-in ve")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Bearer token",
                null,
                "Nhan ticketCode hoac qrToken.",
                "Chi Admin/Manager/Staff duoc check-in.",
                "Ticket phai Active, booking phai Confirmed va da thanh toan du.",
                "Tra ve thong tin ve sau khi da cap nhat checkedInAt/checkedInBy."));

        group.MapPost(CheckOutTicket, "check-out/{codeOrToken}")
            .RequireAuthorization()
            .WithSummary("Check-out ve")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Bearer token",
                null,
                "Nhan ticketCode hoac qrToken.",
                "Chi Admin/Manager/Staff duoc check-out.",
                "Ticket phai da CheckedIn truoc do.",
                "Tra ve thong tin ve sau khi da cap nhat checkedOutAt/checkedOutBy."));

        group.MapPost(ReissueTicket, "reissue/{codeOrToken}")
            .RequireAuthorization()
            .WithSummary("Cap lai ve")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Bearer token",
                """
                {
                  "reason": "QR bi loi, khach co booking hop le"
                }
                """,
                "Nhan ticketCode hoac qrToken.",
                "Chi Admin/Manager/Staff duoc cap lai ve.",
                "Bat buoc nhap reason.",
                "Ve cu chuyen Cancelled, ve moi Active va duoc gan reissue metadata."));

        group.MapGet(QrImage, "qr-image/{codeOrToken}")
            .AllowAnonymous()
            .WithSummary("Tao anh QR ve")
            .WithDescription("Tra ve anh PNG QR cho ticketCode hoac qrToken de chen vao email boarding pass.");
    }

    private static async Task<IResult> ScanTicket(
        ISender sender,
        string codeOrToken,
        CancellationToken ct) =>
        Results.Ok(await sender.Send(new ScanTicketQuery(codeOrToken), ct));

    private static async Task<IResult> CheckInTicket(
        ISender sender,
        string codeOrToken,
        CancellationToken ct) =>
        Results.Ok(await sender.Send(new CheckInTicketCommand(codeOrToken), ct));

    private static async Task<IResult> CheckOutTicket(
        ISender sender,
        string codeOrToken,
        CancellationToken ct) =>
        Results.Ok(await sender.Send(new CheckOutTicketCommand(codeOrToken), ct));

    private static async Task<IResult> ReissueTicket(
        ISender sender,
        string codeOrToken,
        ReissueTicketRequest request,
        CancellationToken ct) =>
        Results.Ok(await sender.Send(new ReissueTicketCommand(codeOrToken, request.Reason), ct));

    private static IResult QrImage(string codeOrToken)
    {
        if (string.IsNullOrWhiteSpace(codeOrToken))
        {
            return Results.BadRequest(new { message = "codeOrToken is required." });
        }

        using var qrGenerator = new QRCodeGenerator();
        using var qrData = qrGenerator.CreateQrCode(codeOrToken.Trim(), QRCodeGenerator.ECCLevel.Q);
        var qrCode = new PngByteQRCode(qrData);
        return Results.File(qrCode.GetGraphic(20), "image/png");
    }
}
