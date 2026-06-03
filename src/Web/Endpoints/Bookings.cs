using SaigonWaterbus.Application.Bookings;

namespace SaigonWaterbus.Web.Endpoints;

public sealed class Bookings : IEndpointGroup
{
    public static string RoutePrefix => "/api/bookings";

    private const string CreateBookingExample =
        """
        {
          "tripId": "8edda809-c649-4d6d-ae54-0f132e4b7311",
          "items": [
            {
              "seatId": "615fa6a0-adf8-4e38-a650-d8b69d11c072",
              "ticketTypeId": "ebcc04d9-0fff-4b2b-9293-745f83670f95",
              "fromTripStopId": "<tripStopId ben len tau>",
              "toTripStopId": "<tripStopId ben xuong tau>",
              "passengerName": "Nguyen Van A",
              "passengerPhone": "0901234567"
            }
          ],
          "promotionCode": null
        }
        """;

    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost(CreateBooking, string.Empty)
            .RequireAuthorization()
            .WithSummary("Dat ve")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Bearer token",
                CreateBookingExample,
                "tripId: lay tu GET /api/trips/search.",
                "items[].seatId: lay tu GET /api/trips/{id}/seats (chi chon ghe co status=Available).",
                "items[].fromTripStopId / toTripStopId: lay tu GET /api/trips/{id} → stops[].tripStopId.",
                "fromTripStopId phai co stop_order < toTripStopId.",
                "ticketTypeId: lay tu GET /api/ticket-types.",
                "promotionCode: null neu khong dung ma giam gia.",
                "Toi da 10 ghe trong 1 lan dat.",
                "Gia tu dong tinh: FareMatrix.BasePrice x TicketType.PriceModifier.",
                "bookingStatus sau khi tao: PendingPayment.",
                "Tra ve 400 neu ghe da bi dat hoac giu truoc do (race condition)."));
    }

    private static async Task<IResult> CreateBooking(
        ISender sender, CreateBookingCommand command, CancellationToken ct) =>
        Results.Ok(await sender.Send(command, ct));
}
