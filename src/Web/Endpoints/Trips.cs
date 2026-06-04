using SaigonWaterbus.Application.Trips;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Web.Endpoints;

public sealed class Trips : IEndpointGroup
{
    public static string RoutePrefix => "/api/trips";

    private const string CreateTripExample =
        """
        {
          "routeCode": "R01-BD-TD",
          "boatCode": "WB-01",
          "operatingDate": "2026-06-10",
          "departureTime": "2026-06-10T08:30:00+07:00"
        }
        """;

    private const string UpdateStatusExample =
        """
        {
          "tripStatus": "Boarding",
          "statusNote": "Tau dang len khach tai Ben Bach Dang"
        }
        """;

    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet(GetTripList, string.Empty)
            .RequireAuthorization()
            .WithSummary("Danh sach chuyen tau (admin)")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Bearer token",
                null,
                "Query params (tat ca optional): operatingDate (yyyy-MM-dd), routeCode (string), status (string).",
                "status hop le: Scheduled | Boarding | Departed | Arrived | Cancelled.",
                "Sap xep: ngay moi nhat → gio khoi hanh tang dan."));

        group.MapGet(SearchTrips, "search")
            .AllowAnonymous()
            .WithSummary("Tim chuyen tau theo hanh trinh va ngay")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Anonymous",
                null,
                "Query params: fromStationId (guid), toStationId (guid), operatingDate (yyyy-MM-dd).",
                "Vi du: GET /api/trips/search?fromStationId=86015ba6-...&toStationId=95916ab1-...&operatingDate=2026-06-04",
                "Chi tra ve chuyen co tripStatus=Scheduled va departureTime > now.",
                "availableSeats = capacitySnapshot - so ghe da giu (SeatHold) - so ghe da dat (BookingItem).",
                "minPrice = basePrice x he_so_nho_nhat cua tat ca loai ve dang active."));

        group.MapGet(GetTripById, "{id:guid}")
            .AllowAnonymous()
            .WithSummary("Chi tiet chuyen tau")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Anonymous",
                null,
                "Tra ve TripDetailDto kem stops[] sap xep theo stop_order.",
                "stops[].tripStopId dung khi tao booking (fromTripStopId, toTripStopId)."));

        group.MapGet(GetTripSeats, "{id:guid}/seats")
            .AllowAnonymous()
            .WithSummary("So do ghe chuyen tau")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Anonymous",
                null,
                "status cua moi ghe: Available | Held | Booked.",
                "Ghe Held: dang duoc giu tam thoi (SeatHold chua het han).",
                "Ghe Booked: da co BookingItem active.",
                "Dung seats[].seatId khi tao booking."));

        group.MapPost(CreateTrip, string.Empty)
            .RequireAuthorization()
            .WithSummary("Tao chuyen tau moi")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Bearer token",
                CreateTripExample,
                "Route phai Active va co it nhat 2 ben dung.",
                "Boat phai Active.",
                "departureTime phai lon hon thoi diem hien tai.",
                "Tu dong tinh gio lich den/di cho tung TripStop dua vao RouteStop.standardTravelMin / standardDwellMin.",
                "tripCode tu sinh: TR-{yyyyMMdd}-{routeCode}-{4 so ngau nhien}."));

        group.MapPatch(UpdateTripStatus, "{id:guid}/status")
            .RequireAuthorization()
            .WithSummary("Cap nhat trang thai chuyen tau")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Bearer token",
                UpdateStatusExample,
                "tripStatus hop le: Scheduled | Boarding | Departed | Arrived | Cancelled.",
                "statusNote: ghi chu kem theo (optional)."));
    }

    private static async Task<IResult> GetTripList(
        ISender sender,
        DateOnly? operatingDate, string? routeCode, string? status,
        CancellationToken ct) =>
        Results.Ok(await sender.Send(new GetTripListQuery(operatingDate, routeCode, status), ct));

    private static async Task<IResult> SearchTrips(
        ISender sender,
        Guid fromStationId, Guid toStationId, DateOnly operatingDate,
        CancellationToken ct) =>
        Results.Ok(await sender.Send(new SearchTripsQuery(fromStationId, toStationId, operatingDate), ct));

    private static async Task<IResult> GetTripById(ISender sender, Guid id, CancellationToken ct) =>
        Results.Ok(await sender.Send(new GetTripDetailQuery(id), ct));

    private static async Task<IResult> GetTripSeats(ISender sender, Guid id, CancellationToken ct) =>
        Results.Ok(await sender.Send(new GetTripAvailableSeatsQuery(id), ct));

    private static async Task<IResult> CreateTrip(ISender sender, CreateTripCommand command, CancellationToken ct) =>
        Results.Ok(await sender.Send(command, ct));

    private static async Task<IResult> UpdateTripStatus(ISender sender, Guid id, UpdateTripStatusRequest req, CancellationToken ct) =>
        Results.Ok(await sender.Send(new UpdateTripStatusCommand(id, req.TripStatus, req.StatusNote), ct));

    public sealed record UpdateTripStatusRequest(TripStatus TripStatus, string? StatusNote);
}
