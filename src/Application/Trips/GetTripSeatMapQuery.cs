using SaigonWaterbus.Application.Bookings;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Application.Fares;
using SaigonWaterbus.Application.Seats;
using SaigonWaterbus.Domain.Entities;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;

namespace SaigonWaterbus.Application.Trips;

public sealed record TripSeatMapSeatDto(
    string SeatNumber,
    int Deck,
    string Row,
    int Column,
    string SeatTypeCode,
    string? SeatTypeName,
    decimal BasePrice,
    string Status);

public sealed record TripSeatMapDto(
    Guid TripId,
    string TripCode,
    Guid? BoatId,
    string? BoatName,
    DateTimeOffset DepartureTime,
    string RouteType,
    bool SellsBySegment,
    int TotalSeats,
    int AvailableSeats,
    int HoldTtlSeconds,
    IReadOnlyList<TripSeatMapSeatDto> Seats,
    string? FromStationCode = null,
    string? ToStationCode = null,
    decimal? SegmentDistanceKm = null,
    // true = đã quá hạn bán vé cho chặng đang xem (tính theo giờ tàu rời bến lên) hoặc chuyến
    // không còn nhận đặt. FE nên khoá thao tác chọn ghế — gọi giữ ghế lúc này sẽ bị từ chối.
    bool IsBookingClosed = false);

// FromStationCode/ToStationCode: chặng khách định đi — trạng thái ghế và giá (trip Regular)
// tính theo chặng đó; bỏ trống = xem cả tuyến (ghế bận nếu có bất kỳ vé nào trên trip).
// SellsBySegment cho FE biết có phải hỏi trạm lên/xuống hay không (false = đi nguyên chuyến).
public sealed record GetTripSeatMapQuery(
    Guid TripId,
    string? FromStationCode = null,
    string? ToStationCode = null) : IRequest<TripSeatMapDto>;

public sealed class GetTripSeatMapQueryHandler : IRequestHandler<GetTripSeatMapQuery, TripSeatMapDto>
{
    public const string StatusAvailable = "Available";
    public const string StatusHeld = "Held";
    public const string StatusHeldByMe = "HeldByMe";
    public const string StatusBooked = "Booked";
    public const string StatusBlocked = "Blocked";

    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;
    private readonly TimeProvider _timeProvider;
    private readonly ISeatHoldService _seatHoldService;

    public GetTripSeatMapQueryHandler(
        IApplicationDbContext context,
        IUserContext userContext,
        TimeProvider timeProvider,
        ISeatHoldService? seatHoldService = null)
    {
        _context = context;
        _userContext = userContext;
        _timeProvider = timeProvider;
        _seatHoldService = seatHoldService ?? NullSeatHoldService.Instance;
    }

    public async Task<TripSeatMapDto> Handle(GetTripSeatMapQuery request, CancellationToken cancellationToken)
    {
        // TripStops cần cho hạn bán vé theo bến khách lên — xem BookingCutoffSupport.
        var trip = await _context.Set<Trip>()
            .AsNoTracking()
            .Include(t => t.Boat)
            .Include(t => t.TripStops)
            .Include(t => t.Route)
                .ThenInclude(r => r.RouteStops)
                    .ThenInclude(rs => rs.Station)
            .SingleOrDefaultAsync(t => t.Id == request.TripId, cancellationToken)
            ?? throw new NotFoundException("Trip not found.");

        var segment = TripSegmentSupport.Resolve(
            trip, request.FromStationCode, request.ToStationCode,
            nameof(request.FromStationCode), nameof(request.ToStationCode));

        // Sơ đồ ghế vẫn xem được sau khi đóng bán (khách tra cứu vé đã mua), chỉ báo cờ để FE
        // khoá thao tác chọn ghế thay vì để họ chọn xong mới bị từ chối ở bước giữ ghế.
        var isBookingClosed = trip.TripStatus != Domain.Enums.TripStatus.Scheduled
            || BookingCutoffSupport.IsPastCutoff(
                trip,
                segment.IsFullTrip ? null : segment.FromStopOrder,
                segment.IsFullTrip ? null : segment.ToStopOrder,
                _timeProvider.GetUtcNow());

        var routeType = trip.Route.RouteType;
        var sellsBySegment = DistanceFareSupport.UsesDistanceFare(trip.TripType, routeType);

        if (!trip.BoatId.HasValue)
        {
            return new TripSeatMapDto(
                trip.Id, trip.TripCode, null, null, trip.DepartureTime,
                routeType, sellsBySegment,
                0, 0, (int)BookingSeatOccupancySupport.SeatPreHoldDuration.TotalSeconds, [],
                IsBookingClosed: isBookingClosed);
        }

        var seats = await _context.Set<Seat>()
            .AsNoTracking()
            .Include(x => x.SeatType)
            .Where(x => x.BoatId == trip.BoatId.Value && x.IsActive)
            .ToListAsync(cancellationToken);

        var seatIds = seats.Select(x => x.Id).ToList();
        var tripSeatsBySeatId = await _context.Set<TripSeat>()
            .AsNoTracking()
            .Where(x => x.TripId == trip.Id && seatIds.Contains(x.SeatId))
            .ToDictionaryAsync(x => x.SeatId, cancellationToken);

        var now = _timeProvider.GetUtcNow();
        var tripSeatIds = tripSeatsBySeatId.Values.Select(x => x.Id).ToList();
        var occupiedTripSeatIds = (await _context.Set<BookingPassenger>()
                .Where(x => x.TripSeatId.HasValue && tripSeatIds.Contains(x.TripSeatId.Value))
                .Where(BookingSeatOccupancySupport.PassengerOccupiesSeat(now))
                .Where(BookingSeatOccupancySupport.PassengerOverlapsSegment(segment.FromStopOrder, segment.ToStopOrder))
                .Select(x => x.TripSeatId!.Value)
                .ToListAsync(cancellationToken))
            .ToHashSet();

        var heldSeats = await _seatHoldService.GetHeldSeatsAsync(trip.Id, cancellationToken);
        var currentUserId = _userContext.UserId;

        // Giá theo km cho ghế STANDARD trên trip Regular: theo chặng đang xem,
        // hoặc cả tuyến khi không truyền chặng. Thiếu km → null, fallback giá theo loại ghế.
        decimal? distanceKm = null;
        decimal? distanceFare = null;
        if (DistanceFareSupport.UsesDistanceFare(trip) && trip.Route is not null)
        {
            var routeStops = trip.Route.RouteStops;
            var fromOrder = segment.IsFullTrip ? routeStops.Min(rs => rs.StopOrder) : segment.FromStopOrder;
            var toOrder = segment.IsFullTrip ? routeStops.Max(rs => rs.StopOrder) : segment.ToStopOrder;
            distanceKm = DistanceFareSupport.TryComputeSegmentDistanceKm(routeStops, fromOrder, toOrder);
            if (distanceKm.HasValue)
            {
                var policy = await DistanceFareSupport.GetActivePolicyAsync(_context, cancellationToken);
                distanceFare = DistanceFareSupport.CalculateFare(policy, distanceKm.Value);
            }
        }

        var seatDtos = seats
            .OrderBy(x => x.Deck)
            .ThenBy(x => x.Row)
            .ThenBy(x => x.Column)
            .Select(seat =>
            {
                tripSeatsBySeatId.TryGetValue(seat.Id, out var tripSeat);
                return new TripSeatMapSeatDto(
                    seat.Code,
                    seat.Deck,
                    seat.Row,
                    seat.Column,
                    seat.SeatTypeCode,
                    seat.SeatType?.Name,
                    ResolveBasePrice(seat, tripSeat, distanceFare),
                    ResolveStatus(tripSeat, occupiedTripSeatIds, heldSeats, currentUserId, segment));
            })
            .ToList();

        return new TripSeatMapDto(
            trip.Id,
            trip.TripCode,
            trip.BoatId,
            trip.Boat?.Name,
            trip.DepartureTime,
            routeType,
            sellsBySegment,
            seatDtos.Count,
            seatDtos.Count(x => x.Status == StatusAvailable),
            (int)BookingSeatOccupancySupport.SeatPreHoldDuration.TotalSeconds,
            seatDtos,
            segment.FromStop?.Station.StationCode,
            segment.ToStop?.Station.StationCode,
            distanceKm,
            isBookingClosed);
    }

    private static decimal ResolveBasePrice(Seat seat, TripSeat? tripSeat, decimal? distanceFare)
    {
        if (distanceFare.HasValue
            && seat.SeatTypeCode.Equals(DistanceFareSupport.DistanceFareSeatTypeCode, StringComparison.OrdinalIgnoreCase))
        {
            return distanceFare.Value;
        }

        if (tripSeat?.Price is > 0)
        {
            return tripSeat.Price.Value;
        }

        if (seat.SeatType is not null)
        {
            return seat.SeatType.BasePrice;
        }

        return SeatTypePricing.TryGetBasePrice(seat.SeatTypeCode, out var basePrice) ? basePrice : 0;
    }

    private static string ResolveStatus(
        TripSeat? tripSeat,
        HashSet<Guid> occupiedTripSeatIds,
        IReadOnlyDictionary<Guid, IReadOnlyList<SeatHoldInfo>> heldSeats,
        Guid? currentUserId,
        TripSegmentSupport.Segment segment)
    {
        if (tripSeat is null || tripSeat.Status == TripSeat.StatusBlocked)
        {
            return StatusBlocked;
        }

        if (occupiedTripSeatIds.Contains(tripSeat.Id))
        {
            return StatusBooked;
        }

        if (heldSeats.TryGetValue(tripSeat.Id, out var holds))
        {
            var overlapping = holds
                .Where(h => BookingSeatOccupancySupport.SegmentsOverlap(
                    h.FromStopOrder, h.ToStopOrder, segment.FromStopOrder, segment.ToStopOrder))
                .ToList();
            if (overlapping.Count > 0)
            {
                return overlapping.All(h => h.UserId == currentUserId) ? StatusHeldByMe : StatusHeld;
            }
        }

        return StatusAvailable;
    }
}
