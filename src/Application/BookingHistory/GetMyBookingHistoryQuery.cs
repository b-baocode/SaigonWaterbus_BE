using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.BookingHistory;

// Type: cách lưu đơn (StandardBooking | CharterBooking).
// ServiceType: dịch vụ khách mua (Waterbus | Sightseeing | Charter) — FE dùng để hiện đúng nhãn
// và màn chi tiết; một StandardBooking có thể là vé waterbus thường hoặc tour ngắm cảnh.
public sealed record BookingHistoryItemDto(
    Guid Id,
    string Type,
    string ServiceType,
    string Code,
    DateTimeOffset CreatedAt,
    DateOnly? DepartureDate,
    TimeOnly? DepartureTime,
    string? FromStationName,
    string? ToStationName,
    int PassengerCount,
    string Status,
    decimal? TotalAmount,
    string? Currency,
    string DetailEndpoint,
    int PointsUsed = 0,
    int PointsEarned = 0,
    Bookings.BookingInsuranceDto? Insurance = null,
    Guid? FromStationId = null,
    Guid? ToStationId = null);

public sealed record GetMyBookingHistoryQuery : IRequest<IReadOnlyList<BookingHistoryItemDto>>;

public sealed class GetMyBookingHistoryQueryHandler
    : IRequestHandler<GetMyBookingHistoryQuery, IReadOnlyList<BookingHistoryItemDto>>
{
    private const string StandardBookingType = "StandardBooking";
    private const string CharterBookingType = "CharterBooking";
    private const string DefaultCurrency = "VND";

    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;

    public GetMyBookingHistoryQueryHandler(IApplicationDbContext context, IUserContext userContext)
    {
        _context = context;
        _userContext = userContext;
    }

    public async Task<IReadOnlyList<BookingHistoryItemDto>> Handle(
        GetMyBookingHistoryQuery request,
        CancellationToken cancellationToken)
    {
        var userId = _userContext.UserId
            ?? throw new ValidationException([]);

        var standardBookings = await _context.Set<Booking>()
            .AsNoTracking()
            .Include(x => x.Passengers)
                .ThenInclude(p => p.FromStation)
            .Include(x => x.Passengers)
                .ThenInclude(p => p.ToStation)
            .Include(x => x.Trip)
                .ThenInclude(x => x!.Route)
                    .ThenInclude(x => x.RouteStops)
                        .ThenInclude(x => x.Station)
            .Include(x => x.Trip)
                .ThenInclude(x => x!.TripStops)
            .Where(x => x.BookingType == Booking.SeatBookingType)
            .Where(x => x.UserId == userId)
            .ToListAsync(cancellationToken);

        var charterBookings = await _context.Set<Booking>()
            .AsNoTracking()
            .Include(x => x.FromStation)
            .Include(x => x.ToStation)
            .Where(x => x.BookingType == Booking.CharterBookingType)
            .Where(x => x.UserId == userId)
            .ToListAsync(cancellationToken);

        return standardBookings
            .Select(CreateStandardBookingHistoryItem)
            .Concat(charterBookings.Select(CreateCharterBookingHistoryItem))
            .OrderByDescending(x => x.CreatedAt)
            .ToArray();
    }

    private static BookingHistoryItemDto CreateStandardBookingHistoryItem(Booking booking)
    {
        var stops = booking.Trip?.Route.RouteStops.OrderBy(x => x.StopOrder).ToArray() ?? [];
        var firstStop = stops.FirstOrDefault();
        var lastStop = stops.LastOrDefault();

        // Chặng của BOOKING (ghế bán theo chặng): ga lên sớm nhất → ga xuống muộn nhất trong các vé
        // chiều đi; giờ theo trip_stops tại 2 bến đó. Dữ liệu cũ chưa lưu chặng → đầu/cuối tuyến như trước.
        var outboundPassengers = booking.Passengers
            .Where(p => p.TripId is null || p.TripId == booking.TripId)
            .ToList();
        var earliestBoarding = outboundPassengers
            .Where(p => p.FromStopOrder.HasValue)
            .OrderBy(p => p.FromStopOrder)
            .FirstOrDefault();
        var latestAlighting = outboundPassengers
            .Where(p => p.ToStopOrder.HasValue)
            .OrderByDescending(p => p.ToStopOrder)
            .FirstOrDefault();

        DateTimeOffset? departure = booking.Trip is null
            ? null
            : Trips.TripStopScheduleSupport.ResolveSegmentTimes(
                booking.Trip, earliestBoarding?.FromStopOrder, latestAlighting?.ToStopOrder).Departure;

        return new BookingHistoryItemDto(
            booking.Id,
            StandardBookingType,
            Bookings.BookingServiceTypes.Resolve(booking.Trip?.Route.RouteType),
            booking.BookingCode,
            booking.Created,
            departure.HasValue ? DateOnly.FromDateTime(departure.Value.LocalDateTime) : null,
            departure.HasValue ? TimeOnly.FromDateTime(departure.Value.LocalDateTime) : null,
            earliestBoarding?.FromStation?.StationName ?? firstStop?.Station.StationName,
            latestAlighting?.ToStation?.StationName ?? lastStop?.Station.StationName,
            booking.Passengers.Count,
            booking.BookingStatus.ToString(),
            booking.TotalAmount,
            DefaultCurrency,
            $"/api/bookings/{booking.Id}",
            booking.PointsUsed,
            booking.PointsEarned,
            Bookings.BookingInsuranceDtoMapper.ToDto((booking.InsuranceSnapshots ?? new List<BookingInsuranceSnapshot>()).FirstOrDefault()),
            earliestBoarding?.FromStationId ?? firstStop?.StationId,
            latestAlighting?.ToStationId ?? lastStop?.StationId);
    }

    private static BookingHistoryItemDto CreateCharterBookingHistoryItem(Booking booking) =>
        new(
            booking.Id,
            CharterBookingType,
            Bookings.BookingServiceTypes.Charter,
            booking.BookingCode,
            booking.Created,
            booking.DepartureDate,
            booking.StartTime,
            booking.FromStation?.StationName,
            booking.ToStation?.StationName,
            booking.PassengerCount.GetValueOrDefault(),
            booking.BookingStatus.ToString(),
            booking.TotalAmount,
            booking.Currency,
            $"/api/charter-bookings/{booking.Id}",
            booking.PointsUsed,
            booking.PointsEarned,
            Bookings.BookingInsuranceDtoMapper.ToDto((booking.InsuranceSnapshots ?? new List<BookingInsuranceSnapshot>()).FirstOrDefault()),
            booking.FromStationId,
            booking.ToStationId);
}
