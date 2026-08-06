using SaigonWaterbus.Application.Bookings;
using SaigonWaterbus.Application.CharterBookings;
using SaigonWaterbus.Application.Tickets;
using QRCoder;
using SaigonWaterbus.Domain.Enums;

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
                "Query optional: source=Qr|Manual|Override, tripStopId, clientOperationId, deviceTime, note.",
                "Scan/tra cuu KHONG doi trang thai ve, co the goi nhieu lan tren cung mot ve.",
                "Response co canCheckIn/canCheckOut de FE biet lan quet nay nen hien nut check-in hay check-out.",
                "Voi booking thuong: TicketScanDto.passengers la nhom duoc QR nay dai dien. QR nguoi lon co the tra ve nguoi lon + INFANT khong ghe di kem.",
                "CHILD co ghe/QR rieng nen quet QR CHILD chi tra ve hanh khach CHILD do; chi INFANT khong ghe moi co usesCompanionTicket=true va companionPassengerId/name tro ve ADULT.",
                "Admin/Manager/Staff xem duoc moi ve.",
                "Neu la Staff thi phai la OnBoard va co ca assignmentType=Boat dang active tren dung tau cua ve.",
                "Customer chi xem duoc ve thuoc booking cua minh.",
                "Charter booking sinh ve theo tung hanh khach sau khi da thanh toan du va nhap danh sach hanh khach."));

        group.MapPost(ScanTicketByBody, "scan")
            .RequireAuthorization()
            .WithSummary("Quet/tra cuu ma ve bang body")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Bearer token",
                """
                {
                  "codeOrToken": "TK2607242A57F0DE",
                  "source": "Qr",
                  "tripStopId": null,
                  "clientOperationId": "scan-uuid",
                  "deviceTime": "2026-07-24T09:00:00+07:00",
                  "note": null
                }
                """,
                "Khuyen dung cho FE scan QR de tranh loi URL path khi token co ky tu dac biet.",
                "codeOrToken nhan ticketCode hoac qrToken ve le; BE cung chap nhan alias ticketCode, qrToken, bookingQrToken.",
                "Neu codeOrToken la QR tong booking thuong hoac bookingCode prefix BK thi tra BookingManifestDto.",
                "Neu codeOrToken la QR tong charter prefix CB thi tra CharterBookingManifestDto.",
                "Neu codeOrToken la ticketCode/qrToken ve le thi tra TicketScanDto.",
                "BookingManifestDto.passengers show full hanh khach cua QR tong. CHILD co ticketCode/qrToken rieng; INFANT khong ghe co usesCompanionTicket=true va dung ticketCode/qrToken cua ADULT.",
                "TicketScanDto co canCheckIn/canCheckOut; scan ve da CheckedIn se canCheckOut=true de FE cho checkout bang cung ma ve.",
                "Quyen va dieu kien ve le giong GET /api/tickets/scan/{codeOrToken}."));

        group.MapPost(CheckInTicket, "check-in/{codeOrToken}")
            .RequireAuthorization()
            .WithSummary("Check-in ve")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Bearer token",
                null,
                "Nhan ticketCode hoac qrToken.",
                "Query optional: source=Qr|Manual|Override, tripStopId, clientOperationId, deviceTime, note.",
                "Chi Admin/Manager/Staff duoc check-in.",
                "Neu la Staff thi phai la OnBoard va co ca assignmentType=Boat dang active tren dung tau cua ve.",
                "Ticket phai Active, booking phai Confirmed va da thanh toan du.",
                "Chi duoc check-in tu 10 phut truoc gio tau roi ben khach len den dung gio roi ben.",
                "Check-in chi thuc hien mot lan; ve da CheckedIn thi scan lai bang /api/tickets/scan de hien canCheckOut=true.",
                "Tra ve thong tin ve sau khi da cap nhat checkedInAt/checkedInBy."));

        group.MapPost(CheckInTicketByBody, "check-in")
            .RequireAuthorization()
            .WithSummary("Check-in ve bang body")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Bearer token",
                """
                {
                  "codeOrToken": "TK2607242A57F0DE",
                  "source": "Qr",
                  "tripStopId": null,
                  "clientOperationId": "checkin-uuid",
                  "deviceTime": "2026-07-24T09:00:00+07:00",
                  "note": null
                }
                """,
                "Khuyen dung cho FE scan QR de tranh loi URL path khi token co ky tu dac biet.",
                "Quyen va dieu kien giong POST /api/tickets/check-in/{codeOrToken}."));

        group.MapPost(RejectTicketConcession, "concession/reject")
            .RequireAuthorization()
            .WithSummary("Reject ve uu dai khong dung doi tuong")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Bearer token",
                """
                {
                  "codeOrToken": "TK2607242A57F0DE",
                  "reason": "Khach khong chung minh du tuoi/khuyet tat",
                  "source": "Qr",
                  "tripStopId": null,
                  "clientOperationId": "reject-concession-uuid",
                  "deviceTime": "2026-07-24T09:00:00+07:00",
                  "note": null
                }
                """,
                "Chi Admin/Manager/Staff duoc reject.",
                "Neu la Staff thi phai la OnBoard va co ca assignmentType=Boat dang active tren dung tau cua ve.",
                "Chi ap dung cho ve SENIOR/DISABLED dang Active trong khung gio check-in.",
                "Waterbus thuong: ve bi chuyen Cancelled.",
                "Sightseeing: hanh khach duoc doi sang ADULT, booking duoc cong chenhlech gia ve nguoi lon va can thanh toan phan con lai truoc khi check-in.",
                "FE co the hien nut nay khi scan DTO co ticketPassenger.passengerType = SENIOR hoac DISABLED; neu xac nhan dung doi tuong thi goi check-in nhu hien tai."));

        group.MapPost(CheckOutTicket, "check-out/{codeOrToken}")
            .RequireAuthorization()
            .WithSummary("Check-out ve")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Bearer token",
                null,
                "Nhan ticketCode hoac qrToken.",
                "Query optional: source=Qr|Manual|Override, tripStopId, clientOperationId, deviceTime, note.",
                "Chi Admin/Manager/Staff duoc check-out.",
                "Neu la Staff thi phai la OnBoard va co ca assignmentType=Boat dang active tren dung tau cua ve.",
                "Ticket phai da CheckedIn truoc do.",
                "Check-out duoc thuc hien sau check-in, nhung khong qua 10 phut sau gio tau den ben khach xuong.",
                "Dung cung ticketCode/qrToken cua ve da check-in; checkout xong ve khong checkout lai duoc.",
                "Tra ve thong tin ve sau khi da cap nhat checkedOutAt/checkedOutBy."));

        group.MapPost(CheckOutTicketByBody, "check-out")
            .RequireAuthorization()
            .WithSummary("Check-out ve bang body")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Bearer token",
                """
                {
                  "codeOrToken": "TK2607242A57F0DE",
                  "source": "Qr",
                  "tripStopId": null,
                  "clientOperationId": "checkout-uuid",
                  "deviceTime": "2026-07-24T09:00:00+07:00",
                  "note": null
                }
                """,
                "Khuyen dung cho FE scan QR de tranh loi URL path khi token co ky tu dac biet.",
                "Quyen va dieu kien giong POST /api/tickets/check-out/{codeOrToken}."));

        group.MapGet(GetTicketScanHistory, "{ticketId:guid}/scan-history")
            .RequireAuthorization()
            .WithSummary("Lịch sử scan/check-in/check-out của vé")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Bearer token",
                null,
                "Admin/Manager/Staff xem được lịch sử vé.",
                "Customer chỉ xem được lịch sử vé thuộc booking của mình.",
                "Dùng cho màn chi tiết vé/audit."));

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
        TicketScanSource? source,
        Guid? tripStopId,
        string? clientOperationId,
        DateTimeOffset? deviceTime,
        string? note,
        CancellationToken ct) =>
        Results.Ok(await sender.Send(
            new ScanTicketQuery(codeOrToken, CreateMetadata(source, tripStopId, clientOperationId, deviceTime, note)),
            ct));

    private static async Task<IResult> ScanTicketByBody(
        ISender sender,
        TicketCodeRequest? request,
        CancellationToken ct)
    {
        var codeOrToken = request?.ResolveCodeOrToken();
        if (string.IsNullOrWhiteSpace(codeOrToken))
        {
            return Results.BadRequest(new { message = "codeOrToken is required." });
        }

        if (codeOrToken.StartsWith("BK", StringComparison.OrdinalIgnoreCase))
        {
            return Results.Ok(await sender.Send(new GetBookingManifestByCodeOrQrTokenQuery(codeOrToken), ct));
        }

        if (codeOrToken.StartsWith("CB", StringComparison.OrdinalIgnoreCase))
        {
            return Results.Ok(await sender.Send(new GetCharterBookingManifestByQrTokenQuery(codeOrToken), ct));
        }

        return Results.Ok(await sender.Send(
            new ScanTicketQuery(codeOrToken, CreateMetadata(request)),
            ct));
    }

    private static async Task<IResult> CheckInTicket(
        ISender sender,
        string codeOrToken,
        TicketScanSource? source,
        Guid? tripStopId,
        string? clientOperationId,
        DateTimeOffset? deviceTime,
        string? note,
        CancellationToken ct) =>
        Results.Ok(await sender.Send(
            new CheckInTicketCommand(codeOrToken, CreateMetadata(source, tripStopId, clientOperationId, deviceTime, note)),
            ct));

    private static async Task<IResult> CheckInTicketByBody(
        ISender sender,
        TicketCodeRequest? request,
        CancellationToken ct)
    {
        var codeOrToken = request?.ResolveCodeOrToken();
        if (string.IsNullOrWhiteSpace(codeOrToken))
        {
            return Results.BadRequest(new { message = "codeOrToken is required." });
        }

        return Results.Ok(await sender.Send(
            new CheckInTicketCommand(codeOrToken, CreateMetadata(request)),
            ct));
    }

    private static async Task<IResult> RejectTicketConcession(
        ISender sender,
        RejectTicketConcessionRequest? request,
        CancellationToken ct)
    {
        var codeOrToken = request?.ResolveCodeOrToken();
        if (string.IsNullOrWhiteSpace(codeOrToken))
        {
            return Results.BadRequest(new { message = "codeOrToken is required." });
        }

        return Results.Ok(await sender.Send(
            new RejectTicketConcessionCommand(
                codeOrToken,
                request?.Reason ?? string.Empty,
                CreateMetadata(request)),
            ct));
    }

    private static async Task<IResult> CheckOutTicket(
        ISender sender,
        string codeOrToken,
        TicketScanSource? source,
        Guid? tripStopId,
        string? clientOperationId,
        DateTimeOffset? deviceTime,
        string? note,
        CancellationToken ct) =>
        Results.Ok(await sender.Send(
            new CheckOutTicketCommand(codeOrToken, CreateMetadata(source, tripStopId, clientOperationId, deviceTime, note)),
            ct));

    private static async Task<IResult> CheckOutTicketByBody(
        ISender sender,
        TicketCodeRequest? request,
        CancellationToken ct)
    {
        var codeOrToken = request?.ResolveCodeOrToken();
        if (string.IsNullOrWhiteSpace(codeOrToken))
        {
            return Results.BadRequest(new { message = "codeOrToken is required." });
        }

        return Results.Ok(await sender.Send(
            new CheckOutTicketCommand(codeOrToken, CreateMetadata(request)),
            ct));
    }

    private static async Task<IResult> GetTicketScanHistory(
        ISender sender,
        Guid ticketId,
        CancellationToken ct) =>
        Results.Ok(await sender.Send(new GetTicketScanHistoryQuery(ticketId), ct));

    private static async Task<IResult> ReissueTicket(
        ISender sender,
        string codeOrToken,
        ReissueTicketRequest request,
        CancellationToken ct) =>
        Results.Ok(await sender.Send(new ReissueTicketCommand(codeOrToken, request.Reason), ct));

    private static IResult QrImage(HttpContext context, string codeOrToken)
    {
        if (string.IsNullOrWhiteSpace(codeOrToken))
        {
            return Results.BadRequest(new { message = "codeOrToken is required." });
        }

        context.Response.Headers["Cache-Control"] = "public, max-age=31536000, immutable";
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";

        using var qrGenerator = new QRCodeGenerator();
        using var qrData = qrGenerator.CreateQrCode(codeOrToken.Trim(), QRCodeGenerator.ECCLevel.Q);
        var qrCode = new PngByteQRCode(qrData);
        return Results.File(qrCode.GetGraphic(20), "image/png");
    }

    private static TicketScanRequestMetadata CreateMetadata(
        TicketScanSource? source,
        Guid? tripStopId,
        string? clientOperationId,
        DateTimeOffset? deviceTime,
        string? note) =>
        new(
            source ?? TicketScanSource.Qr,
            TripStopId: tripStopId,
            ClientOperationId: clientOperationId,
            DeviceTime: deviceTime,
            Note: note);

    private static TicketScanRequestMetadata CreateMetadata(TicketCodeRequest? request) =>
        CreateMetadata(
            request?.Source,
            request?.TripStopId,
            request?.ClientOperationId,
            request?.DeviceTime,
            request?.Note);

    private static TicketScanRequestMetadata CreateMetadata(RejectTicketConcessionRequest? request) =>
        CreateMetadata(
            request?.Source,
            request?.TripStopId,
            request?.ClientOperationId,
            request?.DeviceTime,
            request?.Note);

    public sealed record TicketCodeRequest(
        string? CodeOrToken = null,
        string? TicketCode = null,
        string? QrToken = null,
        string? BookingQrToken = null,
        TicketScanSource? Source = null,
        Guid? TripStopId = null,
        string? ClientOperationId = null,
        DateTimeOffset? DeviceTime = null,
        string? Note = null)
    {
        public string? ResolveCodeOrToken() =>
            FirstNonBlank(CodeOrToken, TicketCode, QrToken, BookingQrToken)?.Trim();

        private static string? FirstNonBlank(params string?[] values) =>
            values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
    }

    public sealed record RejectTicketConcessionRequest(
        string? CodeOrToken = null,
        string? TicketCode = null,
        string? QrToken = null,
        string? Reason = null,
        TicketScanSource? Source = null,
        Guid? TripStopId = null,
        string? ClientOperationId = null,
        DateTimeOffset? DeviceTime = null,
        string? Note = null)
    {
        public string? ResolveCodeOrToken() =>
            FirstNonBlank(CodeOrToken, TicketCode, QrToken)?.Trim();

        private static string? FirstNonBlank(params string?[] values) =>
            values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
    }
}
