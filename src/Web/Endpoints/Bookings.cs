using SaigonWaterbus.Application.Bookings;

namespace SaigonWaterbus.Web.Endpoints;

public sealed class Bookings : IEndpointGroup
{
    public static string RoutePrefix => "/api/bookings";

    private const string CreateBookingExample =
        """
        {
          "tripCode": "TR-20260610-R01-BD-LD-1234",
          "items": [
            {
              "seatNumber": "A1",
              "ticketTypeCode": "ADULT",
              "fromStationCode": "BD",
              "toStationCode": "TADA",
              "passengerName": "Nguyen Van A",
              "passengerPhone": "0901234567",
              "passengerEmail": "nguyenvana@example.com"
            },
            {
              "seatNumber": null,
              "ticketTypeCode": "INFANT",
              "fromStationCode": "BD",
              "toStationCode": "TADA",
              "passengerName": "Be Nguyen Van B",
              "birthYear": 2025
            }
          ],
          "promotionCode": null
        }
        """;

    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet(GetBookings, string.Empty)
            .RequireAuthorization()
            .WithSummary("Lich su dat ve cua toi")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Bearer token",
                null,
                "Tra ve tat ca booking cua user dang dang nhap, moi nhat truoc.",
                "itemCount: so ve con hieu luc (chua bi cancel)."));

        group.MapGet(GetBookingDetail, "{id:guid}")
            .RequireAuthorization()
            .WithSummary("Chi tiet booking")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Bearer token",
                null,
                "Tra ve day du thong tin booking kem danh sach ve (hanh khach, cho, gio len/xuong).",
                "Tra ve 404 neu booking khong thuoc ve user dang dang nhap."));

        group.MapPost(CancelBooking, "{id:guid}/cancel")
            .RequireAuthorization()
            .WithSummary("Huy booking")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Bearer token",
                null,
                "Huy toan bo booking va tat ca BookingItem ben trong.",
                "Khong the huy khi tau da khoi hanh (departureTime <= now).",
                "Hoan lai luot su dung ma khuyen mai neu co.",
                "Tra ve 204 khi huy thanh cong."));

        group.MapGet(GetBookingManifestByCode, "manifest/{bookingCode}")
            .RequireAuthorization()
            .WithSummary("Tra cuu manifest booking bang ma dat cho")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Chu booking hoac Admin/Manager/Staff",
                null,
                "Tra ve danh sach hanh khach + ve + trang thai check-in cua booking thuong.",
                "bookingQrToken: QR chung cua booking (chi co sau khi thanh toan du)."));

        group.MapGet(GetBookingManifestByQrToken, "manifest/qr/{qrToken}")
            .RequireAuthorization()
            .WithSummary("Tra cuu manifest booking bang QR chung")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Chu booking hoac Admin/Manager/Staff",
                null,
                "Staff quet QR chung tren ve dien tu de mo manifest ca nhom.",
                "Check-in tung ve van dung POST /api/tickets/check-in/{codeOrToken}."));

        group.MapPost(CheckInAllBookingTickets, "manifest/qr/{qrToken}/check-in-all")
            .RequireAuthorization()
            .WithSummary("Check-in ca nhom bang QR chung")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin, Manager hoac Staff",
                null,
                "Check-in mot luot toan bo ve Active cua booking thuong.",
                "Yeu cau booking da Confirmed va thanh toan du.",
                "Tra ve manifest moi sau khi check-in."));

        group.MapPost(ResendBookingTickets, "{id:guid}/resend-tickets")
            .RequireAuthorization()
            .WithSummary("Gui lai email ve dien tu (QR)")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Chu booking hoac Admin/Manager/Staff",
                null,
                "Gui lai email ve: nguoi dat nhan email tong (QR chung + tat ca QR rieng); hanh khach co email nhan boarding pass rieng.",
                "Chi ap dung cho booking thuong da Confirmed va thanh toan du.",
                "Neu ve/QR chung chua duoc phat hanh (vd webhook loi) se phat hanh bu truoc khi gui."));

        group.MapPost(CreateBooking, string.Empty)
            .RequireAuthorization()
            .WithSummary("Dat ve")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Bearer token",
                CreateBookingExample,
                "tripCode: lay tu GET /api/trips hoac GET /api/trips/search → tripCode.",
                "seatNumber: lay tu GET /api/trips/{id}/seats → seats[].seatNumber (chi chon ghe status=Available).",
                "ticketTypeCode: ADULT (nguyen gia) | INFANT (duoi 2 tuoi) | SENIOR (tren 70) | DISABLED (khuyet tat) - 3 loai sau mien phi, chi ap dung ghe STANDARD (khong ap dung sightseeing).",
                "INFANT co the bo trong seatNumber (ngoi cung nguoi lon, khong chiem ghe) hoac chon ghe rieng - deu mien phi va van co ve QR.",
                "fromStationCode / toStationCode: lay tu GET /api/trips/{id} → stops[].stationCode.",
                "fromStationCode phai co stop_order nho hon toStationCode.",
                "Toi da 10 ghe trong 1 lan dat.",
                "Gia tu dong tinh theo gia cua seatTypeCode cua ghe x ticket type modifier.",
                "passengerEmail (optional): hanh khach co email se nhan rieng ve dien tu (QR) cua minh sau khi thanh toan.",
                "bookingStatus sau khi tao: PendingPayment; ghe duoc giu 15 phut (holdExpiresAt), qua han booking tu Expired va nha ghe.",
                "Tra ve 400 neu ghe da bi dat hoac dang duoc nguoi khac tam giu (race condition)."));
    }

    private static async Task<IResult> GetBookings(ISender sender, CancellationToken ct) =>
        Results.Ok(await sender.Send(new GetBookingListQuery(), ct));

    private static async Task<IResult> GetBookingDetail(ISender sender, Guid id, CancellationToken ct) =>
        Results.Ok(await sender.Send(new GetBookingDetailQuery(id), ct));

    private static async Task<IResult> CancelBooking(ISender sender, Guid id, CancellationToken ct)
    {
        await sender.Send(new CancelBookingCommand(id), ct);
        return Results.NoContent();
    }

    private static async Task<IResult> CreateBooking(
        ISender sender, CreateBookingCommand command, CancellationToken ct) =>
        Results.Ok(await sender.Send(command, ct));

    private static async Task<IResult> GetBookingManifestByCode(
        ISender sender, string bookingCode, CancellationToken ct) =>
        Results.Ok(await sender.Send(new GetBookingManifestByCodeQuery(bookingCode), ct));

    private static async Task<IResult> GetBookingManifestByQrToken(
        ISender sender, string qrToken, CancellationToken ct) =>
        Results.Ok(await sender.Send(new GetBookingManifestByQrTokenQuery(qrToken), ct));

    private static async Task<IResult> CheckInAllBookingTickets(
        ISender sender, string qrToken, CancellationToken ct) =>
        Results.Ok(await sender.Send(new CheckInAllBookingTicketsCommand(qrToken), ct));

    private static async Task<IResult> ResendBookingTickets(
        ISender sender, Guid id, CancellationToken ct) =>
        Results.Ok(await sender.Send(new ResendBookingTicketsCommand(id), ct));
}
