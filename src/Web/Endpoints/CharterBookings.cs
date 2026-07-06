using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using QRCoder;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SaigonWaterbus.Application.CharterBookings;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Web.Endpoints;

public sealed class CharterBookings : IEndpointGroup
{
    public static string RoutePrefix => "/api/charter-bookings";
    public static string OpenApiTag => "CharterBookings";

    private const string CreateCharterBookingExample =
        """
        {
          "departureDate": "2026-08-01",
          "rentalUnit": "Day",
          "durationValue": 1,
          "adultCount": 15,
          "childCount": 5,
          "startTime": "08:00:00",
          "fromStationId": null,
          "toStationId": null,
          "itineraryStops": null,
          "requestedBoats": [
            { "seatSetupType": "StandardAndVip" },
            { "seatSetupType": "FullStandard" }
          ],
          "boatRequirements": "Muốn tàu StandardAndVip và không gian tổ chức sinh nhật",
          "specialRequests": "Can trang tri sinh nhat"
        }
        """;

    private const string QuoteCharterBookingExample =
        """
        {
          "boatId": "00000000-0000-0000-0000-000000000000",
          "subtotalAmount": null,
          "rentalUnit": "Day",
          "durationValue": 1,
          "promotionCode": "SUMMER10"
        }
        """;

    private const string UpdateStatusExample =
        """
        {
          "bookingStatus": "Completed"
        }
        """;

    private const string UpdatePassengersExample =
        """
        {
          "passengers": [
            {
              "fullName": "Nguyen Van A",
              "dateOfBirth": "20/05/1995"
            },
            {
              "fullName": "Tran Thi B",
              "dateOfBirth": "02/09/2016"
            }
          ]
        }
        """;

    private const string AttendanceExample =
        """
        {
          "action": "CheckIn",
          "mode": "All",
          "ticketIds": null
        }
        """;

    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet(GetAdminCharterBookings, "admin")
            .RequireAuthorization()
            .WithSummary("Admin xem danh sach charter booking")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin",
                null,
                "Tra ve tat ca charter booking de admin xu ly yeu cau, nhap tau va chot gia."));

        group.MapGet(GetAdminCharterBookingDetail, "admin/{id:guid}")
            .RequireAuthorization()
            .WithSummary("Admin xem chi tiet charter booking")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin",
                null,
                "Admin xem chi tiet charter booking bat ky, khong bi gioi han theo owner."));

        group.MapPatch(UpdateAdminCharterBookingStatus, "admin/{id:guid}/status")
            .RequireAuthorization()
            .Accepts<UpdateCharterBookingStatusRequest>("application/json")
            .WithSummary("Admin doi trang thai charter booking")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin",
                UpdateStatusExample,
                "Doi bookingStatus cho charter booking hien co.",
                "Khong dung endpoint nay de chot gia; muon sang Quoted khi chua co gia/tau thi dung PUT /api/charter-bookings/admin/{id}/quote.",
                "Confirmed yeu cau booking da co thanh toan dat coc hoac thanh toan du.",
                "Completed yeu cau paymentStatus = Paid.",
                "Refunded yeu cau paymentStatus = Refunded; muon hoan tien thi dung API refund truoc.",
                "Status hop le cho charter booking: PendingQuote, Quoted, Confirmed, Cancelled, Expired, Completed, Refunded."));

        group.MapPut(QuoteCharterBooking, "admin/{id:guid}/quote")
            .RequireAuthorization()
            .Accepts<QuoteCharterBookingRequest>("application/json")
            .WithSummary("Admin nhap tau va chot gia charter booking")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin",
                QuoteCharterBookingExample,
                "Dung khi customer da gui yeu cau charter booking nhung chua co tau/gia.",
                "boatId: thay bang ID tau that tu GET /api/boats; tau phai Active va du suc chua theo adultCount + childCount customer da nhap.",
                "subtotalAmount: optional; bo trong/null de backend tu tinh theo gia tau, thoi gian/quang duong; gui so tien neu admin muon override gia chot.",
                "Hour: backend can co du lieu km/thoi gian tu GeoJSON/toa do ben; neu thieu thi tra 400 hoac admin phai nhap subtotalAmount thu cong.",
                "Hour: so gio tinh tien = max(durationValue, thoi gian hanh trinh lam tron len gio).",
                "Day: neu co du lieu km/thoi gian thi so ngay tinh tien = max(durationValue, thoi gian hanh trinh lam tron theo block 12 gio). Neu thieu km/thoi gian thi tinh theo dailyRentalPrice * durationValue.",
                "promotionCode: tuy chon; gui chuoi rong de bo promotion hien tai.",
                "Sau khi quote thanh cong, bookingStatus = Quoted va customer moi tao payment duoc.",
                "Khong cho quote neu booking da co payment Pending/Paid."));

        group.MapGet(GetCharterBookingManifestByCode, "manifest/{bookingCode}")
            .RequireAuthorization()
            .WithSummary("Tra cuu manifest charter booking bang ma dat cho")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Bearer token",
                null,
                "Nhap bookingCode/ma dat cho chung de xem thong tin charter booking, tau, lich trinh, danh sach hanh khach va trang thai tung ve.",
                "Admin/Manager/Staff tra cuu duoc moi charter booking.",
                "Customer chi tra cuu duoc charter booking cua minh.",
                "BookingCode chi dung de tra cuu manifest; check-in/check-out van dung ticketCode hoac qrToken cua tung hanh khach."));

        group.MapGet(GetCharterBookingManifestByQrToken, "manifest/qr/{qrToken}")
            .RequireAuthorization()
            .WithSummary("Tra cuu manifest charter booking bang QR tong")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Bearer token",
                null,
                "Nhan charterBookingQrToken cua charter booking.",
                "Admin/Manager/Staff dung khi quet QR tong de mo manifest doan.",
                "Customer chi xem duoc charter booking cua minh.",
                "QR tong chi mo manifest; check-in/check-out van dung ticketCode hoac qrToken cua tung hanh khach."));

        group.MapGet(CharterBookingQrImage, "qr-image/{qrToken}")
            .AllowAnonymous()
            .WithSummary("Tao anh QR tong charter booking")
            .WithDescription("Tra ve anh PNG QR cho charterBookingQrToken de hien thi tren UI/email.");

        group.MapPost(UpdateCharterBookingAttendance, "manifest/qr/{qrToken}/attendance")
            .RequireAuthorization()
            .Accepts<CharterBookingAttendanceRequest>("application/json")
            .WithSummary("Check-in/check-out charter booking bang QR tong")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin/Manager/Staff",
                AttendanceExample,
                "Dung sau khi quet QR tong charter booking.",
                "action: CheckIn hoac CheckOut.",
                "mode: All de BE tu chon tat ca ve hanh khach; Selected de chi xu ly ticketIds gui len.",
                "API cap nhat tung ve rieng le va tra ve manifest moi sau khi xu ly.",
                "Ve sai trang thai duoc tra ve trong skippedTickets, khong lam fail toan bo request."));

        group.MapGet(GetCharterBookings, string.Empty)
            .RequireAuthorization()
            .WithSummary("Danh sach yeu cau thue tau cua toi")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Bearer token",
                null,
                "Tra ve tat ca yeu cau thue tron tau (charter booking) cua user dang dang nhap, moi nhat truoc."));

        group.MapGet(GetCharterBookingDetail, "{id:guid}")
            .RequireAuthorization()
            .WithSummary("Chi tiet yeu cau thue tau")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Bearer token",
                null,
                "Tra ve day du thong tin mot yeu cau thue tau.",
                "Tra ve 404 neu khong thuoc ve user dang dang nhap."));

        group.MapPost(CancelCharterBooking, "{id:guid}/cancel")
            .RequireAuthorization()
            .WithSummary("Huy yeu cau thue tau")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Bearer token",
                null,
                "Huy mot yeu cau thue tau con hieu luc.",
                "Khong the huy khi da Completed/Refunded.",
                "Tra ve 204 khi huy thanh cong."));

        group.MapPut(UpdateCharterBookingPassengers, "{id:guid}/passengers")
            .RequireAuthorization()
            .Accepts<UpdateCharterBookingPassengersRequest>("application/json")
            .WithSummary("Nhap/cap nhat danh sach hanh khach len tau")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Bearer token",
                UpdatePassengersExample,
                "Thay the toan bo danh sach hanh khach cua charter booking.",
                "Moi hanh khach chi can fullName va dateOfBirth.",
                "Backend tu tinh passengerType: Adult tu 12 tuoi tro len, Child duoi 12 tuoi.",
                "So hanh khach khong duoc vuot qua passengerCount da dang ky.",
                "Sau khi luu thanh cong, response tra ve tickets[] gom ticketCode/qrToken cho tung hanh khach."));

        group.MapPost(ImportCharterBookingPassengers, "{id:guid}/passengers/import")
            .RequireAuthorization()
            .DisableAntiforgery()
            .Accepts<IFormFile>("multipart/form-data")
            .WithSummary("Upload file danh sach hanh khach len tau")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Bearer token",
                null,
                "Chi upload sau khi charter booking da thanh toan du: PaymentStatus = Paid.",
                "Gui multipart/form-data voi field file.",
                "Ho tro .xlsx, .csv, .tsv, .txt.",
                "File chi can cot ten hanh khach va ngay sinh. Header chap nhan: fullName/name/ho ten va dateOfBirth/dob/ngay sinh.",
                "Backend tu tinh adultCount/childCount theo moc 12 tuoi.",
                "Sau khi import thanh cong, response tra ve tickets[] gom ticketCode/qrToken cho tung hanh khach."));

        group.MapGet(ExportCharterBookingTickets, "{id:guid}/tickets/export")
            .RequireAuthorization()
            .Produces(StatusCodes.Status200OK, contentType: "application/zip")
            .WithSummary("Export tat ca ve/QR charter booking")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Bearer token",
                null,
                "Tra ve file ZIP gom tickets.csv va anh PNG QR cho tung ve.",
                "Chi export sau khi charter booking da thanh toan du va da nhap/upload danh sach hanh khach.",
                "File export khong duoc luu thanh ban rieng; backend tao lai tu bang tickets moi lan goi API."));

        group.MapPost(ExportSelectedCharterBookingTickets, "{id:guid}/tickets/export")
            .RequireAuthorization()
            .Produces(StatusCodes.Status200OK, contentType: "application/zip")
            .Accepts<CharterBookingTicketSelectionRequest>("application/json")
            .WithSummary("Export ve/QR charter booking theo ticketIds")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Bearer token",
                """
                {
                  "ticketIds": [
                    "00000000-0000-0000-0000-000000000000"
                  ]
                }
                """,
                "Tra ve file ZIP gom tickets.csv va anh PNG QR cho cac ve duoc chon.",
                "Bo trong ticketIds hoac gui mang rong de export tat ca ve hop le."));

        group.MapPost(PrintCharterBookingTickets, "{id:guid}/tickets/print")
            .RequireAuthorization()
            .Produces(StatusCodes.Status200OK, contentType: "text/html")
            .Accepts<CharterBookingTicketSelectionRequest>("application/json")
            .WithSummary("In ve charter booking theo ticketIds")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Bearer token",
                """
                {
                  "ticketIds": [
                    "00000000-0000-0000-0000-000000000000"
                  ]
                }
                """,
                "Tra ve trang HTML A4 co QR cho cac ve duoc chon de browser in hoac save as PDF.",
                "Bo trong ticketIds hoac gui mang rong de in tat ca ve hop le."));

        group.MapPost(ExportCharterBookingTicketsPdf, "{id:guid}/tickets/pdf")
            .RequireAuthorization()
            .Produces(StatusCodes.Status200OK, contentType: "application/pdf")
            .Accepts<CharterBookingTicketSelectionRequest>("application/json")
            .WithSummary("Export PDF ve charter booking theo ticketIds")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Bearer token",
                """
                {
                  "ticketIds": [
                    "00000000-0000-0000-0000-000000000000"
                  ]
                }
                """,
                "Tra ve file PDF A4 gom cac ve duoc chon, moi ve co QR rieng.",
                "Bo trong ticketIds hoac gui mang rong de export tat ca ve hop le.",
                "File PDF khong duoc luu thanh ban rieng; backend tao lai tu bang tickets moi lan goi API."));

        group.MapGet(ExportCharterBookingTicketsPdfByQrToken, "tickets/pdf/{qrToken}")
            .AllowAnonymous()
            .Produces(StatusCodes.Status200OK, contentType: "application/pdf")
            .WithSummary("Tai PDF ve charter booking bang QR token")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Public link",
                null,
                "Dung cho nut tai PDF trong email boarding pass.",
                "qrToken la ma bi mat cua ve da gui qua email.",
                "Tra ve PDF tat ca ve hop le cua charter booking da thanh toan du."));

        group.MapPost(CreateCharterBooking, string.Empty)
            .RequireAuthorization()
            .Accepts<CreateCharterBookingRequest>("application/json")
            .WithSummary("Tao yeu cau thue tron tau")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Bearer token",
                CreateCharterBookingExample,
                "Customer khong chon tau. Admin se nhap tau va chot gia bang API quote.",
                "departureDate: phai cach ngay tao booking it nhat 7 ngay.",
                "rentalUnit: Hour hoac Day, la don vi thue khach mong muon.",
                "durationValue: so gio hoac so ngay thue (1-60).",
                "adultCount / childCount: so nguoi lon va tre em khach du kien di; passengerCount backend tu tinh.",
                "requestedBoats: danh sach tau customer muon thue; moi item co seatSetupType.",
                "seatSetupType: FullStandard = tau full ghe STANDARD, StandardAndVip = tau setup sightseeing voi ghe CABIN/RIVER/SKY.",
                "preferredSeatSetupType van duoc ho tro cho client cu va duoc hieu nhu 1 tau.",
                "boatRequirements: yeu cau ghi chu them de admin chon tau.",
                "fromStationId / toStationId: tuy chon, de null neu chua chon ben; neu dien thi lay id that tu GET /api/stations.",
                "itineraryStops: tuy chon, de null neu khong co diem dung; neu dien thi stationId phai la id that tu GET /api/stations va stopOrder khong trung.",
                "Sau khi thanh toan du, co the upload file danh sach bang POST /api/charter-bookings/{id}/passengers/import.",
                "bookingStatus sau khi tao: PendingQuote."));
    }

    private static async Task<IResult> GetAdminCharterBookings(ISender sender, CancellationToken ct) =>
        Results.Ok(await sender.Send(new GetAdminCharterBookingListQuery(), ct));

    private static async Task<IResult> GetAdminCharterBookingDetail(ISender sender, Guid id, CancellationToken ct) =>
        Results.Ok(await sender.Send(new GetAdminCharterBookingDetailQuery(id), ct));

    private static async Task<IResult> GetCharterBookingManifestByCode(
        ISender sender,
        string bookingCode,
        CancellationToken ct) =>
        Results.Ok(await sender.Send(new GetCharterBookingManifestByCodeQuery(bookingCode), ct));

    private static async Task<IResult> GetCharterBookingManifestByQrToken(
        ISender sender,
        string qrToken,
        CancellationToken ct) =>
        Results.Ok(await sender.Send(new GetCharterBookingManifestByQrTokenQuery(qrToken), ct));

    private static IResult CharterBookingQrImage(string qrToken)
    {
        if (string.IsNullOrWhiteSpace(qrToken))
        {
            return Results.BadRequest(new { message = "qrToken is required." });
        }

        return Results.File(BuildQrPngBytes(qrToken.Trim()), "image/png");
    }

    private static async Task<IResult> UpdateCharterBookingAttendance(
        ISender sender,
        string qrToken,
        CharterBookingAttendanceRequest request,
        CancellationToken ct) =>
        Results.Ok(await sender.Send(new UpdateCharterBookingAttendanceCommand(
            qrToken,
            request.Action,
            request.Mode,
            request.TicketIds), ct));

    private static async Task<IResult> UpdateAdminCharterBookingStatus(
        ISender sender,
        Guid id,
        UpdateCharterBookingStatusRequest request,
        CancellationToken ct) =>
        Results.Ok(await sender.Send(new UpdateCharterBookingStatusCommand(id, request.BookingStatus), ct));

    private static async Task<IResult> QuoteCharterBooking(
        ISender sender,
        Guid id,
        QuoteCharterBookingRequest request,
        CancellationToken ct) =>
        Results.Ok(await sender.Send(new QuoteCharterBookingCommand(
            id,
            request.BoatId,
            request.SubtotalAmount,
            request.RentalUnit,
            request.DurationValue,
            request.PromotionCode), ct));

    private static async Task<IResult> GetCharterBookings(ISender sender, CancellationToken ct) =>
        Results.Ok(await sender.Send(new GetCharterBookingListQuery(), ct));

    private static async Task<IResult> GetCharterBookingDetail(ISender sender, Guid id, CancellationToken ct) =>
        Results.Ok(await sender.Send(new GetCharterBookingDetailQuery(id), ct));

    private static async Task<IResult> CancelCharterBooking(ISender sender, Guid id, CancellationToken ct)
    {
        await sender.Send(new CancelCharterBookingCommand(id), ct);
        return Results.NoContent();
    }

    private static async Task<IResult> UpdateCharterBookingPassengers(
        ISender sender,
        Guid id,
        UpdateCharterBookingPassengersRequest request,
        CancellationToken ct) =>
        Results.Ok(await sender.Send(new UpdateCharterBookingPassengersCommand(id, request.Passengers), ct));

    private static async Task<IResult> ImportCharterBookingPassengers(
        ISender sender,
        Guid id,
        HttpRequest request,
        CancellationToken ct)
    {
        if (!request.HasFormContentType)
        {
            return Results.BadRequest(new { message = "Gui multipart/form-data voi field file." });
        }

        var form = await request.ReadFormAsync(ct);
        var file = form.Files.GetFile("file") ?? form.Files.FirstOrDefault();
        if (file is null || file.Length == 0)
        {
            return Results.BadRequest(new { message = "File danh sach hanh khach la bat buoc." });
        }

        await using var fileStream = file.OpenReadStream();
        using var memoryStream = new MemoryStream();
        await fileStream.CopyToAsync(memoryStream, ct);

        return Results.Ok(await sender.Send(
            new ImportCharterBookingPassengersCommand(id, file.FileName, memoryStream.ToArray()),
            ct));
    }

    private static async Task<IResult> ExportCharterBookingTickets(
        ISender sender,
        Guid id,
        CancellationToken ct) =>
        await BuildTicketExportResultAsync(sender, id, ticketIds: null, ct);

    private static async Task<IResult> ExportSelectedCharterBookingTickets(
        ISender sender,
        Guid id,
        CharterBookingTicketSelectionRequest request,
        CancellationToken ct) =>
        await BuildTicketExportResultAsync(sender, id, request.TicketIds, ct);

    private static async Task<IResult> PrintCharterBookingTickets(
        ISender sender,
        Guid id,
        CharterBookingTicketSelectionRequest request,
        CancellationToken ct)
    {
        var export = await sender.Send(
            new ExportCharterBookingTicketsQuery(id, request.TicketIds),
            ct);
        return Results.Content(BuildPrintableTicketsHtml(export), "text/html; charset=utf-8");
    }

    private static async Task<IResult> ExportCharterBookingTicketsPdf(
        ISender sender,
        [FromServices] ICharterBookingTicketPdfRenderer pdfRenderer,
        Guid id,
        CharterBookingTicketSelectionRequest request,
        CancellationToken ct)
    {
        var export = await sender.Send(
            new ExportCharterBookingTicketsQuery(id, request.TicketIds),
            ct);
        var pdfBytes = pdfRenderer.Render(export);

        return Results.File(
            pdfBytes,
            "application/pdf",
            $"{SanitizeFileName(export.BookingCode)}-tickets.pdf");
    }

    private static async Task<IResult> ExportCharterBookingTicketsPdfByQrToken(
        ISender sender,
        [FromServices] ICharterBookingTicketPdfRenderer pdfRenderer,
        string qrToken,
        CancellationToken ct)
    {
        var export = await sender.Send(
            new ExportCharterBookingTicketsByQrTokenQuery(qrToken),
            ct);
        var pdfBytes = pdfRenderer.Render(export);

        return Results.File(
            pdfBytes,
            "application/pdf",
            $"{SanitizeFileName(export.BookingCode)}-tickets.pdf");
    }

    private static async Task<IResult> BuildTicketExportResultAsync(
        ISender sender,
        Guid id,
        IReadOnlyCollection<Guid>? ticketIds,
        CancellationToken ct)
    {
        var export = await sender.Send(new ExportCharterBookingTicketsQuery(id, ticketIds), ct);
        var zipBytes = await BuildTicketExportZipAsync(export, ct);
        return Results.File(
            zipBytes,
            "application/zip",
            $"{SanitizeFileName(export.BookingCode)}-tickets.zip");
    }

    private static async Task<IResult> CreateCharterBooking(
        ISender sender, CreateCharterBookingRequest request, CancellationToken ct) =>
        Results.Ok(await sender.Send(new CreateCharterBookingCommand(
            request.DepartureDate,
            request.RentalUnit,
            request.DurationValue,
            request.AdultCount,
            request.ChildCount,
            request.StartTime,
            request.FromStationId,
            request.ToStationId,
            request.ItineraryStops,
            request.RequestedBoats,
            request.PreferredSeatSetupType,
            request.BoatRequirements,
            request.SpecialRequests), ct));

    private static async Task<byte[]> BuildTicketExportZipAsync(
        CharterBookingTicketExportDto export,
        CancellationToken ct)
    {
        using var memoryStream = new MemoryStream();
        using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var qrFileNames = export.Tickets.ToDictionary(
                x => x.TicketId,
                x => $"qr/{SanitizeFileName(x.TicketCode)}.png");

            var csvEntry = archive.CreateEntry("tickets.csv", CompressionLevel.Optimal);
            await using (var csvStream = csvEntry.Open())
            {
                var csv = BuildTicketsCsv(export, qrFileNames);
                var preamble = Encoding.UTF8.GetPreamble();
                await csvStream.WriteAsync(preamble, ct);
                await csvStream.WriteAsync(Encoding.UTF8.GetBytes(csv), ct);
            }

            using var qrGenerator = new QRCodeGenerator();
            foreach (var ticket in export.Tickets)
            {
                ct.ThrowIfCancellationRequested();
                using var qrData = qrGenerator.CreateQrCode(ticket.QrToken, QRCodeGenerator.ECCLevel.Q);
                var qrCode = new PngByteQRCode(qrData);
                var qrBytes = qrCode.GetGraphic(20);

                var qrEntry = archive.CreateEntry(qrFileNames[ticket.TicketId], CompressionLevel.Optimal);
                await using var qrStream = qrEntry.Open();
                await qrStream.WriteAsync(qrBytes, ct);
            }
        }

        return memoryStream.ToArray();
    }

    private static string BuildPrintableTicketsHtml(CharterBookingTicketExportDto export)
    {
        var builder = new StringBuilder();
        builder.AppendLine("<!doctype html>");
        builder.AppendLine("<html lang=\"vi\">");
        builder.AppendLine("<head>");
        builder.AppendLine("<meta charset=\"utf-8\">");
        builder.AppendLine("<title>Ve len tau " + Html(export.BookingCode) + "</title>");
        builder.AppendLine("""
        <style>
        @page { size: A4; margin: 12mm; }
        * { box-sizing: border-box; }
        body { margin: 0; color: #111827; font-family: Arial, Helvetica, sans-serif; }
        .sheet { display: grid; grid-template-columns: minmax(0, 1fr); gap: 10mm; }
        .ticket { min-height: 120mm; border: 1px solid #9ca3af; padding: 8mm; break-inside: avoid; page-break-inside: avoid; display: grid; grid-template-rows: auto 1fr auto; gap: 6mm; }
        .top { display: flex; justify-content: space-between; gap: 8mm; border-bottom: 1px solid #d1d5db; padding-bottom: 5mm; }
        .brand { font-size: 17px; font-weight: 700; }
        .code { font-size: 13px; font-weight: 700; text-align: right; }
        .info { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 3mm 8mm; font-size: 12px; line-height: 1.35; }
        .label { color: #6b7280; font-size: 10px; text-transform: uppercase; letter-spacing: .04em; }
        .value { font-weight: 700; overflow-wrap: anywhere; }
        .itinerary { grid-column: 1 / -1; border-top: 1px solid #e5e7eb; margin-top: 2mm; padding-top: 3mm; }
        .stops { margin: 2mm 0 0; padding-left: 5mm; }
        .stops li { margin: 1mm 0; }
        .qr { display: grid; place-items: center; gap: 3mm; border-top: 1px solid #d1d5db; padding-top: 5mm; }
        .qr img { width: 42mm; height: 42mm; image-rendering: pixelated; }
        .token { font-size: 8px; color: #4b5563; overflow-wrap: anywhere; text-align: center; }
        @media print { .ticket { page-break-inside: avoid; } }
        </style>
        """);
        builder.AppendLine("</head>");
        builder.AppendLine("<body>");
        builder.AppendLine("<main class=\"sheet\">");

        foreach (var ticket in export.Tickets)
        {
            builder.AppendLine("<section class=\"ticket\">");
            builder.AppendLine("<div class=\"top\">");
            builder.AppendLine("<div><div class=\"brand\">Waterbus</div><div class=\"label\">Ve len tau</div></div>");
            builder.AppendLine("<div class=\"code\">" + Html(ticket.TicketCode) + "</div>");
            builder.AppendLine("</div>");
            builder.AppendLine("<div class=\"info\">");
            AppendInfo(builder, "Booking", export.BookingCode);
            AppendInfo(builder, "So ve", ticket.TicketCode);
            AppendInfo(builder, "Hanh khach", ticket.PassengerName ?? "Khach hang");
            AppendInfo(builder, "Ngay sinh", ticket.PassengerDateOfBirth?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? string.Empty);
            AppendInfo(builder, "Loai khach", ticket.PassengerType ?? string.Empty);
            AppendInfo(builder, "Tau", export.BoatName ?? string.Empty);
            AppendInfo(builder, "Ngay di", export.DepartureDate?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? string.Empty);
            AppendInfo(builder, "Gio di", export.StartTime?.ToString("HH:mm", CultureInfo.InvariantCulture) ?? string.Empty);
            AppendInfo(builder, "Tu", export.FromStationName ?? string.Empty);
            AppendInfo(builder, "Den", export.ToStationName ?? string.Empty);
            AppendInfo(builder, "Trang thai ve", ticket.TicketStatus);
            AppendItinerary(builder, export);
            builder.AppendLine("</div>");
            builder.AppendLine("<div class=\"qr\">");
            builder.AppendLine("<img alt=\"QR " + Html(ticket.TicketCode) + "\" src=\"" + BuildQrDataUri(ticket.QrToken) + "\">");
            builder.AppendLine("<div class=\"token\">" + Html(ticket.QrToken) + "</div>");
            builder.AppendLine("</div>");
            builder.AppendLine("</section>");
        }

        builder.AppendLine("</main>");
        builder.AppendLine("</body>");
        builder.AppendLine("</html>");
        return builder.ToString();
    }

    private static byte[] BuildPrintableTicketsPdf(CharterBookingTicketExportDto export)
    {
        QuestPDF.Settings.License = LicenseType.Community;
        const string navy = "#073B56";
        const string teal = "#057C9F";
        const string gold = "#F1C85D";
        const string ink = "#283640";
        const string muted = "#6B7780";
        const string pale = "#F3FAFC";
        const string line = "#D9E7EE";

        return Document.Create(document =>
        {
            foreach (var ticket in export.Tickets)
            {
                var qrBytes = BuildQrPngBytes(ticket.QrToken);
                var departureDate = FormatPdfDate(export.DepartureDate);
                var startTime = FormatPdfTime(export.StartTime);
                var fromStation = ResolvePdfText(export.FromStationName, "Diem di");
                var toStation = ResolvePdfText(export.ToStationName, "Diem den");
                var vesselName = ResolvePdfText(export.BoatName, "Waterbus");
                var passengerName = ResolvePdfText(ticket.PassengerName, "Khach hang");
                var passengerType = ResolvePdfText(ticket.PassengerType, "Passenger");
                var birthDate = FormatPdfDate(ticket.PassengerDateOfBirth);

                document.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(28);
                    page.DefaultTextStyle(x => x.FontSize(10).FontColor(ink));

                    page.Content()
                        .Background("#EEF6F8")
                        .Padding(18)
                        .Column(column =>
                        {
                            column.Spacing(0);

                            column.Item()
                                .Background(gold)
                                .Height(8);

                            column.Item()
                                .Background(navy)
                                .PaddingHorizontal(24)
                                .PaddingVertical(20)
                                .Row(row =>
                                {
                                    row.RelativeItem().Column(header =>
                                    {
                                        header.Item().Text("WATERBUS")
                                            .FontSize(20)
                                            .Bold()
                                            .FontColor(Colors.White);
                                        header.Item().PaddingTop(4).Text("BOARDING PASS / VE LEN TAU")
                                            .FontSize(9)
                                            .SemiBold()
                                            .FontColor(gold);
                                    });

                                    row.ConstantItem(250).AlignRight().Column(code =>
                                    {
                                        code.Item().Text("TICKET CODE")
                                            .FontSize(7)
                                            .SemiBold()
                                            .FontColor("#B8D7E4");
                                        code.Item().Text(ticket.TicketCode)
                                            .FontSize(14)
                                            .Bold()
                                            .FontColor(Colors.White);
                                        code.Item().PaddingTop(4).Text($"Booking {export.BookingCode}")
                                            .FontSize(8)
                                            .FontColor("#B8D7E4");
                                    });
                                });

                            column.Item()
                                .Background(Colors.White)
                                .Padding(24)
                                .Column(ticketBody =>
                                {
                                    ticketBody.Spacing(18);

                                    ticketBody.Item()
                                        .Border(1)
                                        .BorderColor(line)
                                        .Background(pale)
                                        .Padding(16)
                                        .Row(route =>
                                        {
                                            route.RelativeItem().Column(from =>
                                            {
                                                from.Item().Text("FROM").FontSize(7).SemiBold().FontColor(muted);
                                                from.Item().Text(fromStation).FontSize(18).Bold().FontColor(teal);
                                            });

                                            route.ConstantItem(48).AlignCenter().Column(mid =>
                                            {
                                                mid.Item().AlignCenter().Text("->").FontSize(20).Bold().FontColor(gold);
                                                mid.Item().AlignCenter().Text("ROUTE").FontSize(6).FontColor(muted);
                                            });

                                            route.RelativeItem().AlignRight().Column(to =>
                                            {
                                                to.Item().AlignRight().Text("TO").FontSize(7).SemiBold().FontColor(muted);
                                                to.Item().AlignRight().Text(toStation).FontSize(18).Bold().FontColor(teal);
                                            });
                                        });

                                    ticketBody.Item().Row(main =>
                                    {
                                        main.RelativeItem().Column(details =>
                                        {
                                            details.Spacing(12);

                                            details.Item()
                                                .BorderLeft(4)
                                                .BorderColor(gold)
                                                .PaddingLeft(12)
                                                .Column(passenger =>
                                                {
                                                    passenger.Item().Text("PASSENGER / HANH KHACH")
                                                        .FontSize(7)
                                                        .SemiBold()
                                                        .FontColor(muted);
                                                    passenger.Item().Text(passengerName)
                                                        .FontSize(24)
                                                        .Bold()
                                                        .FontColor(ink);
                                                });

                                            details.Item().Table(table =>
                                            {
                                                table.ColumnsDefinition(columns =>
                                                {
                                                    columns.RelativeColumn();
                                                    columns.RelativeColumn();
                                                });

                                                AddPdfInfoCell(table.Cell(), "Date", departureDate, teal);
                                                AddPdfInfoCell(table.Cell(), "Time", startTime, teal);
                                                AddPdfInfoCell(table.Cell(), "Vessel", vesselName, ink);
                                                AddPdfInfoCell(table.Cell(), "Passenger type", passengerType, ink);
                                                AddPdfInfoCell(table.Cell(), "Date of birth", birthDate, ink);
                                                AddPdfInfoCell(table.Cell(), "Ticket status", ticket.TicketStatus, ink);
                                            });
                                        });

                                        main.ConstantItem(170)
                                            .Border(1)
                                            .BorderColor(line)
                                            .Padding(14)
                                            .AlignCenter()
                                            .Column(qr =>
                                            {
                                                qr.Spacing(8);
                                                qr.Item().Text("SCAN TO BOARD")
                                                    .FontSize(8)
                                                    .SemiBold()
                                                    .FontColor(navy)
                                                    .AlignCenter();
                                                qr.Item().Width(136).Height(136).AlignCenter().Image(qrBytes).FitArea();
                                                qr.Item().Text("Quet QR de check-in")
                                                    .FontSize(7)
                                                    .FontColor(muted)
                                                    .AlignCenter();
                                            });
                                    });

                                    if (export.ItineraryStops.Count > 0)
                                    {
                                        ticketBody.Item()
                                            .BorderTop(1)
                                            .BorderColor(line)
                                            .PaddingTop(14)
                                            .Column(stops =>
                                            {
                                                stops.Spacing(6);
                                                stops.Item().Text("ITINERARY / LICH TRINH")
                                                    .FontSize(9)
                                                    .Bold()
                                                    .FontColor(navy);

                                                foreach (var stop in export.ItineraryStops.OrderBy(x => x.StopOrder))
                                                {
                                                    var duration = stop.StayDurationMinutes > 0
                                                        ? $" - {stop.StayDurationMinutes} min"
                                                        : string.Empty;
                                                    var note = string.IsNullOrWhiteSpace(stop.Note)
                                                        ? string.Empty
                                                        : $" - {stop.Note.Trim()}";

                                                    stops.Item().Row(stopRow =>
                                                    {
                                                        stopRow.ConstantItem(20)
                                                            .Background(gold)
                                                            .PaddingVertical(3)
                                                            .AlignCenter()
                                                            .Text(stop.StopOrder.ToString(CultureInfo.InvariantCulture))
                                                            .FontSize(8)
                                                            .Bold()
                                                            .FontColor(navy);
                                                        stopRow.RelativeItem()
                                                            .PaddingLeft(8)
                                                            .Text($"{stop.StationName}{duration}{note}")
                                                            .FontSize(9)
                                                            .FontColor(ink);
                                                    });
                                                }
                                            });
                                    }

                                    ticketBody.Item()
                                        .BorderTop(1)
                                        .BorderColor(line)
                                        .PaddingTop(10)
                                        .Row(footer =>
                                        {
                                            footer.RelativeItem().Text("Please keep this boarding pass for verification. Vui long giu ve nay de lam thu tuc len tau.")
                                                .FontSize(8)
                                                .FontColor(muted);
                                            footer.ConstantItem(120).AlignRight().Text("waterbus")
                                                .FontSize(8)
                                                .Bold()
                                                .FontColor(teal);
                                        });
                                });
                        });
                });
            }
        }).GeneratePdf();
    }

    private static void AppendItinerary(StringBuilder builder, CharterBookingTicketExportDto export)
    {
        if (export.ItineraryStops.Count == 0)
        {
            return;
        }

        builder.AppendLine("<div class=\"itinerary\">");
        builder.AppendLine("<div class=\"label\">Lich trinh chi tiet</div>");
        builder.AppendLine("<ol class=\"stops\">");
        foreach (var stop in export.ItineraryStops)
        {
            var duration = stop.StayDurationMinutes > 0
                ? $" - dung {stop.StayDurationMinutes} phut"
                : string.Empty;
            var note = string.IsNullOrWhiteSpace(stop.Note)
                ? string.Empty
                : $" - {stop.Note.Trim()}";
            builder.AppendLine("<li><span class=\"value\">"
                + Html(stop.StationName)
                + "</span>"
                + Html(duration + note)
                + "</li>");
        }

        builder.AppendLine("</ol>");
        builder.AppendLine("</div>");
    }

    private static void AppendInfo(StringBuilder builder, string label, string value)
    {
        builder.AppendLine("<div>");
        builder.AppendLine("<div class=\"label\">" + Html(label) + "</div>");
        builder.AppendLine("<div class=\"value\">" + Html(value) + "</div>");
        builder.AppendLine("</div>");
    }

    private static string BuildQrDataUri(string qrToken)
    {
        return "data:image/png;base64," + Convert.ToBase64String(BuildQrPngBytes(qrToken));
    }

    private static byte[] BuildQrPngBytes(string qrToken)
    {
        using var qrGenerator = new QRCodeGenerator();
        using var qrData = qrGenerator.CreateQrCode(qrToken, QRCodeGenerator.ECCLevel.Q);
        var qrCode = new PngByteQRCode(qrData);
        return qrCode.GetGraphic(20);
    }

    private static void AddPdfInfoCell(IContainer container, string label, string value, string valueColor)
    {
        container
            .PaddingBottom(10)
            .PaddingRight(10)
            .Column(column =>
        {
            column.Item().Text(label.ToUpperInvariant()).FontSize(7).SemiBold().FontColor("#7C8B95");
            column.Item().Text(ResolvePdfText(value)).FontSize(11).Bold().FontColor(valueColor);
        });
    }

    private static string FormatPdfDate(DateOnly? date) =>
        date?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? "-";

    private static string FormatPdfTime(TimeOnly? time) =>
        time?.ToString("HH:mm", CultureInfo.InvariantCulture) ?? "-";

    private static string ResolvePdfText(string? value, string fallback = "-") =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static string BuildTicketsCsv(
        CharterBookingTicketExportDto export,
        IReadOnlyDictionary<Guid, string> qrFileNames)
    {
        var builder = new StringBuilder();
        builder.AppendLine("bookingCode,ticketCode,qrToken,ticketStatus,passengerName,dateOfBirth,passengerType,qrImageFile");
        foreach (var ticket in export.Tickets)
        {
            builder.Append(Csv(export.BookingCode)).Append(',');
            builder.Append(Csv(ticket.TicketCode)).Append(',');
            builder.Append(Csv(ticket.QrToken)).Append(',');
            builder.Append(Csv(ticket.TicketStatus)).Append(',');
            builder.Append(Csv(ticket.PassengerName)).Append(',');
            builder.Append(Csv(ticket.PassengerDateOfBirth?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))).Append(',');
            builder.Append(Csv(ticket.PassengerType)).Append(',');
            builder.Append(Csv(qrFileNames[ticket.TicketId])).AppendLine();
        }

        return builder.ToString();
    }

    private static string Csv(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value.IndexOfAny([',', '"', '\r', '\n']) < 0
            ? value
            : $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }

    private static string Html(string value) => WebUtility.HtmlEncode(value);

    private static string SanitizeFileName(string value)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            builder.Append(invalidChars.Contains(character) ? '-' : character);
        }

        return builder.Length == 0 ? "charter-booking" : builder.ToString();
    }
}
