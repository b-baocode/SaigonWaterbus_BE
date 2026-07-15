using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Application.Seats;
using SaigonWaterbus.Application.TicketTypes;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Application.Trips;

public sealed record SearchTripsQuery(
    Guid FromStationId,
    Guid ToStationId,
    DateOnly OperatingDate,
    string? RouteType = null) : IRequest<IReadOnlyList<TripSummaryDto>>;

public sealed class SearchTripsQueryValidator : AbstractValidator<SearchTripsQuery>
{
    public SearchTripsQueryValidator()
    {
        RuleFor(x => x.RouteType)
            .Must(RouteTypes.IsValid)
            .WithMessage($"routeType chi nhan {RouteTypes.Regular} hoac {RouteTypes.SightseeingLoop}.")
            .When(x => !string.IsNullOrWhiteSpace(x.RouteType));

        RuleFor(x => x.RouteType)
            .Must(x =>
            {
                var routeType = RouteTypes.Normalize(x);
                return routeType != RouteTypes.Charter
                    && routeType != RouteTypes.CharterReference;
            })
            .WithMessage("Chuyen charter khong ban ve le nen khong tim kiem duoc.")
            .When(x => !string.IsNullOrWhiteSpace(x.RouteType));
    }
}

public sealed class SearchTripsQueryHandler : IRequestHandler<SearchTripsQuery, IReadOnlyList<TripSummaryDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly TimeProvider _timeProvider;

    public SearchTripsQueryHandler(IApplicationDbContext context, TimeProvider timeProvider)
    {
        _context = context;
        _timeProvider = timeProvider;
    }

    public async Task<IReadOnlyList<TripSummaryDto>> Handle(SearchTripsQuery request, CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();

        var validRouteIds = await _context.Set<RouteStop>()
            .Where(rs => rs.StationId == request.FromStationId && rs.IsPickupAllowed)
            .Join(_context.Set<RouteStop>().Where(rs => rs.StationId == request.ToStationId && rs.IsDropoffAllowed),
                from => from.RouteId,
                to => to.RouteId,
                (from, to) => new { from, to })
            .Where(x => x.from.StopOrder < x.to.StopOrder)
            .Select(x => new { RouteId = x.from.RouteId, FromStop = x.from, ToStop = x.to })
            .ToListAsync(cancellationToken);

        if (validRouteIds.Count == 0)
            return [];

        var routeIds = validRouteIds.Select(x => x.RouteId).ToList();

        var tripQuery = _context.Set<Trip>()
            .Include(t => t.Route)
                .ThenInclude(r => r.RouteStops)
            .Where(t => routeIds.Contains(t.RouteId)
                     && t.Route.IsBookable
                     // Trip charter thue tron tau khong ban ve le.
                     && t.TripType == TripTypes.Regular
                     && t.OperatingDate == request.OperatingDate
                     && t.TripStatus == TripStatus.Scheduled
                     && t.DepartureTime > now);

        if (!string.IsNullOrWhiteSpace(request.RouteType))
        {
            var routeType = RouteTypes.Normalize(request.RouteType);
            tripQuery = tripQuery.Where(t => t.Route.RouteType == routeType);
        }

        var trips = await tripQuery.ToListAsync(cancellationToken);

        var tripIds = trips.Select(t => t.Id).ToList();

        // Chặng tìm kiếm (stop order) theo từng route — ghế bán theo chặng nên chỗ trống
        // của một chuyến phụ thuộc đoạn khách muốn đi.
        var searchSegmentByRouteId = validRouteIds
            .GroupBy(x => x.RouteId)
            .ToDictionary(
                g => g.Key,
                g => (FromOrder: g.First().FromStop.StopOrder, ToOrder: g.First().ToStop.StopOrder));

        // Đếm theo trip của từng ghế (TripSeat.TripId) thay vì Booking.TripId — booking khứ hồi
        // có ghế trên 2 trip. Chỉ đếm hành khách chiếm ghế (INFANT ngồi cùng người lớn không trừ chỗ),
        // và chỉ tính ghế có vé GIAO CHẶNG tìm kiếm (vé cũ không có chặng = chiếm cả trip).
        // Đếm distinct ghế vì một ghế có thể có nhiều vé trên các đoạn khác nhau.
        var occupyingPassengers = await _context.Set<BookingPassenger>()
            .Where(p => p.TripSeatId.HasValue && tripIds.Contains(p.TripSeat!.TripId))
            .Where(Bookings.BookingSeatOccupancySupport.PassengerOccupiesSeat(now))
            .Select(p => new
            {
                p.TripSeat!.TripId,
                TripSeatId = p.TripSeatId!.Value,
                p.FromStopOrder,
                p.ToStopOrder
            })
            .ToListAsync(cancellationToken);

        var routeIdByTripId = trips.ToDictionary(t => t.Id, t => t.RouteId);
        var bookedCounts = occupyingPassengers
            .Where(p =>
            {
                var segment = searchSegmentByRouteId[routeIdByTripId[p.TripId]];
                return Bookings.BookingSeatOccupancySupport.SegmentsOverlap(
                    p.FromStopOrder ?? int.MinValue, p.ToStopOrder ?? int.MaxValue,
                    segment.FromOrder, segment.ToOrder);
            })
            .GroupBy(p => p.TripId)
            .ToDictionary(g => g.Key, g => g.Select(p => p.TripSeatId).Distinct().Count());

        // Giá min theo chuyến: ưu tiên giá đã chốt trong trip_seats (đặt khi tạo trip).
        var tripSeatMinPrices = await _context.Set<TripSeat>()
            .Where(ts => tripIds.Contains(ts.TripId) && ts.Price != null && ts.Price > 0)
            .GroupBy(ts => ts.TripId)
            .Select(g => new { TripId = g.Key, MinPrice = g.Min(x => x.Price!.Value) })
            .ToDictionaryAsync(x => x.TripId, x => x.MinPrice, cancellationToken);

        var boatIds = trips
            .Where(x => x.BoatId.HasValue)
            .Select(x => x.BoatId!.Value)
            .Distinct()
            .ToList();
        var seatRows = await _context.Set<Seat>()
            .Where(x => boatIds.Contains(x.BoatId))
            .Include(x => x.SeatType)
            .Select(x => new { x.BoatId, x.IsActive, x.SeatTypeCode, BasePriceFromDb = (decimal?)x.SeatType!.BasePrice })
            .ToListAsync(cancellationToken);
        var seatStats = seatRows
            .GroupBy(x => x.BoatId)
            .ToDictionary(
                g => g.Key,
                g => new
                {
                    ActiveSeatCount = g.Count(x => x.IsActive),
                    MinSeatPrice = g
                        .Where(x => x.IsActive)
                        .Select(x => x.BasePriceFromDb ?? (SeatTypePricing.TryGetBasePrice(x.SeatTypeCode, out var p) ? p : (decimal?)null))
                        .Where(p => p.HasValue)
                        .Min()
                });
        // Giá "từ" = loại vé trả tiền rẻ nhất (bỏ qua các loại miễn phí).
        var minModifier = TicketTypePricing.All
            .Where(x => x.PriceModifier > 0)
            .Min(x => x.PriceModifier);

        // Trip Regular tính giá theo quãng đường: giá "từ" = giá chặng tìm kiếm (nếu tuyến đã
        // nhập đủ km); fallback giá theo loại ghế như cũ khi thiếu km.
        var farePolicy = trips.Any(Fares.DistanceFareSupport.UsesDistanceFare)
            ? await Fares.DistanceFareSupport.GetActivePolicyAsync(_context, cancellationToken)
            : null;

        return trips.OrderBy(t => t.DepartureTime).Select(t =>
        {
            var booked = bookedCounts.GetValueOrDefault(t.Id, 0);
            seatStats.TryGetValue(t.BoatId ?? Guid.Empty, out var stats);
            var capacity = stats?.ActiveSeatCount ?? t.CapacitySnapshot;
            var available = capacity - booked;

            decimal? segmentFare = null;
            if (farePolicy is not null && Fares.DistanceFareSupport.UsesDistanceFare(t))
            {
                var segment = searchSegmentByRouteId[t.RouteId];
                var distanceKm = Fares.DistanceFareSupport.TryComputeSegmentDistanceKm(
                    t.Route.RouteStops, segment.FromOrder, segment.ToOrder);
                if (distanceKm.HasValue)
                {
                    segmentFare = Fares.DistanceFareSupport.CalculateFare(farePolicy, distanceKm.Value);
                }
            }

            var minBasePrice = segmentFare
                ?? (tripSeatMinPrices.TryGetValue(t.Id, out var tripSeatMin)
                    ? tripSeatMin
                    : stats?.MinSeatPrice is > 0 ? stats.MinSeatPrice.Value : (decimal?)null);
            var minPrice = minBasePrice is > 0 ? minBasePrice * minModifier : null;

            return new TripSummaryDto(
                t.Id, t.TripCode, t.Route.RouteName, t.Route.RouteType,
                t.DepartureTime, t.ArrivalTime,
                t.DepartureTime, t.ArrivalTime,
                Math.Max(0, available), capacity,
                minPrice, t.TripStatus.ToString());
        }).ToList();
    }
}
