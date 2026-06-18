using System.Globalization;
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
          "capacity": 50,
          "operatingDate": "10/06/2026",
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
                "Query params (tat ca optional): operatingDate (dd/MM/yyyy hoac dd-MM-yyyy), routeCode (string), status (string).",
                "status hop le: Scheduled | Boarding | Departed | Arrived | Cancelled.",
                "Sap xep: ngay moi nhat → gio khoi hanh tang dan."));

        group.MapGet(SearchTrips, "search")
            .AllowAnonymous()
            .WithSummary("Tim chuyen tau theo hanh trinh va ngay")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Anonymous",
                null,
                "Query params: fromStationId (guid), toStationId (guid), operatingDate (dd/MM/yyyy hoac dd-MM-yyyy).",
                "Chi tra ve chuyen co tripStatus=Scheduled va departureTime > now.",
                "availableSeats = capacitySnapshot - so ve da dat (BookingItem active)."));

        group.MapGet(GetTripById, "{id:guid}")
            .AllowAnonymous()
            .WithSummary("Chi tiet chuyen tau")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Anonymous",
                null,
                "Tra ve TripDetailDto kem stops[] sap xep theo stop_order."));

        group.MapPost(CreateTrip, string.Empty)
            .RequireAuthorization()
            .WithSummary("Tao chuyen tau moi")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Bearer token",
                CreateTripExample,
                "Route phai Active va co it nhat 2 ben dung.",
                "departureTime phai lon hon thoi diem hien tai.",
                "capacity: so hanh khach toi da cua chuyen.",
                "tripCode tu sinh: TR-{yyyyMMdd}-{routeCode}-{4 so ngau nhien}."));

        group.MapGet(GetTripSeats, "{id:guid}/seats")
            .AllowAnonymous()
            .WithSummary("So do ghe cua chuyen tau")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Anonymous",
                null,
                "Tra ve so do ghe grouped theo tang (deck) va hang (row).",
                "Moi ghe co SeatTypeCode, SeatTypeName.",
                "TotalSeats = tong so ghe; BookedSeats = da dat; AvailableSeats = con trong.",
                "Chi co TripSeat neu trip duoc tao tu TripSchedule hoac co vessel."));

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
        string? operatingDate, string? routeCode, string? status,
        CancellationToken ct)
    {
        if (!TryParseOptionalDateOnly(operatingDate, out var parsedOperatingDate))
        {
            return Results.BadRequest(new { message = "operatingDate phải có định dạng dd/MM/yyyy, dd-MM-yyyy hoặc yyyy-MM-dd." });
        }

        return Results.Ok(await sender.Send(new GetTripListQuery(parsedOperatingDate, routeCode, status), ct));
    }

    private static async Task<IResult> SearchTrips(
        ISender sender,
        Guid fromStationId, Guid toStationId, string operatingDate,
        CancellationToken ct)
    {
        if (!TryParseRequiredDateOnly(operatingDate, out var parsedOperatingDate))
        {
            return Results.BadRequest(new { message = "operatingDate phải có định dạng dd/MM/yyyy, dd-MM-yyyy hoặc yyyy-MM-dd." });
        }

        return Results.Ok(await sender.Send(new SearchTripsQuery(fromStationId, toStationId, parsedOperatingDate), ct));
    }

    private static async Task<IResult> GetTripById(ISender sender, Guid id, CancellationToken ct) =>
        Results.Ok(await sender.Send(new GetTripDetailQuery(id), ct));

    private static async Task<IResult> GetTripSeats(ISender sender, Guid id, CancellationToken ct) =>
        Results.Ok(await sender.Send(new GetTripSeatsQuery(id), ct));

    private static async Task<IResult> CreateTrip(ISender sender, CreateTripCommand command, CancellationToken ct) =>
        Results.Ok(await sender.Send(command, ct));

    private static async Task<IResult> UpdateTripStatus(ISender sender, Guid id, UpdateTripStatusRequest req, CancellationToken ct) =>
        Results.Ok(await sender.Send(new UpdateTripStatusCommand(id, req.TripStatus, req.StatusNote), ct));

    public sealed record UpdateTripStatusRequest(TripStatus TripStatus, string? StatusNote);

    private static bool TryParseOptionalDateOnly(string? value, out DateOnly? date)
    {
        date = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        if (!TryParseRequiredDateOnly(value, out var parsedDate))
        {
            return false;
        }

        date = parsedDate;
        return true;
    }

    private static bool TryParseRequiredDateOnly(string? value, out DateOnly date)
    {
        date = default;
        return DateOnly.TryParseExact(
            value,
            ["dd/MM/yyyy", "dd-MM-yyyy", "yyyy-MM-dd"],
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out date);
    }
}
