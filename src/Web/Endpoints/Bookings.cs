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
              "passengerPhone": "0901234567"
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

        group.MapPost(CreateBooking, string.Empty)
            .RequireAuthorization()
            .WithSummary("Dat ve")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Bearer token",
                CreateBookingExample,
                "tripCode: lay tu GET /api/trips hoac GET /api/trips/search → tripCode.",
                "seatNumber: lay tu GET /api/trips/{id}/seats → seats[].seatNumber (chi chon ghe status=Available).",
                "ticketTypeCode: lay tu GET /api/ticket-types → ticketTypeCode (vi du: ADULT, STUDENT).",
                "fromStationCode / toStationCode: lay tu GET /api/trips/{id} → stops[].stationCode.",
                "fromStationCode phai co stop_order nho hon toStationCode.",
                "Toi da 10 ghe trong 1 lan dat.",
                "Gia tu dong tinh: FareMatrix.BasePrice x TicketType.PriceModifier.",
                "bookingStatus sau khi tao: PendingPayment.",
                "Tra ve 400 neu ghe da bi dat hoac giu truoc do (race condition)."));
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
}
