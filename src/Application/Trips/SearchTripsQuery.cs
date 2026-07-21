using SaigonWaterbus.Application.Common;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Application.Seats;
using SaigonWaterbus.Application.TicketTypes;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Application.Trips;

/// <summary>
/// Tìm chuyến waterbus thường (route Regular) theo chặng đi + ngày.
/// Chuyến sightseeing tìm bằng <see cref="SearchSightseeingTripsQuery"/> riêng.
/// </summary>
public sealed record SearchTripsQuery(
    Guid FromStationId,
    Guid ToStationId,
    DateOnly OperatingDate) : IRequest<IReadOnlyList<TripSummaryDto>>;

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
        // Ẩn các chuyến đã qua hạn bán vé (đóng bán trước giờ khởi hành) — khớp với chặn ở tạo booking.
        var bookingCutoff = now + BookingExpirationPolicy.BookingCutoffBeforeDeparture;

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

        var searchSegmentsByRouteId = validRouteIds
            .GroupBy(x => new { x.RouteId, FromOrder = x.FromStop.StopOrder })
            .Select(g => new
            {
                g.Key.RouteId,
                Segment = new SearchSegment(g.Key.FromOrder, g.Min(x => x.ToStop.StopOrder))
            })
            .GroupBy(x => x.RouteId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<SearchSegment>)g
                    .Select(x => x.Segment)
                    .OrderBy(x => x.FromOrder)
                    .ThenBy(x => x.ToOrder)
                    .ToList());

        var routeIds = validRouteIds.Select(x => x.RouteId).ToList();

        var tripQuery = _context.Set<Trip>()
            .Include(t => t.Route)
                .ThenInclude(r => r.RouteStops)
            .Where(t => routeIds.Contains(t.RouteId)
                     && t.Route.IsBookable
                     // Endpoint nay chi tim waterbus thuong; sightseeing co endpoint rieng,
                     // charter thue tron tau khong ban ve le.
                     && t.Route.RouteType == RouteTypes.Regular
                     && t.TripType == TripTypes.Regular
                     && t.OperatingDate == request.OperatingDate
                     && TripBookingAvailabilitySupport.CustomerBookableStatuses.Contains(t.TripStatus)
                     // Chỉ loại chuyến đã chạy xong. Hạn đóng bán tính theo giờ rời BẾN KHÁCH LÊN
                     // (không biểu diễn được trong SQL) nên lọc sau khi có giờ từng bến.
                     && t.ArrivalTime > now);

        var trips = await tripQuery.ToListAsync(cancellationToken);

        var tripIds = trips.Select(t => t.Id).ToList();

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
        var occupyingPassengersByTripId = occupyingPassengers
            .GroupBy(p => p.TripId)
            .ToDictionary(g => g.Key, g => g.ToList());

        // Giờ đi/đến theo CHẶNG tìm kiếm: lấy từ trip_stops (giờ dự kiến từng bến của chuyến);
        // trip cũ chưa có trip_stops thì suy từ route stops như BuildStopDtos.
        var tripStopsByTripId = (await _context.Set<TripStop>()
                .Where(ts => tripIds.Contains(ts.TripId))
                .Select(ts => new { ts.TripId, ts.StopOrder, ts.PlannedArrivalTime, ts.PlannedDepartureTime })
                .ToListAsync(cancellationToken))
            .GroupBy(ts => ts.TripId)
            .ToDictionary(
                g => g.Key,
                g => g.ToDictionary(
                    ts => ts.StopOrder,
                    ts => (Arrival: ts.PlannedArrivalTime, Departure: ts.PlannedDepartureTime)));

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
            var selectedSegment = ResolveOpenSearchSegment(
                t, searchSegmentsByRouteId[t.RouteId], tripStopsByTripId, bookingCutoff);
            if (selectedSegment is null)
            {
                return null;
            }

            var tripPassengers = occupyingPassengersByTripId.GetValueOrDefault(t.Id) ?? [];
            var booked = tripPassengers
                .Where(p => Bookings.BookingSeatOccupancySupport.SegmentsOverlap(
                    p.FromStopOrder ?? int.MinValue, p.ToStopOrder ?? int.MaxValue,
                    selectedSegment.Segment.FromOrder, selectedSegment.Segment.ToOrder))
                .Select(p => p.TripSeatId)
                .Distinct()
                .Count();
            seatStats.TryGetValue(t.BoatId ?? Guid.Empty, out var stats);
            var capacity = stats?.ActiveSeatCount ?? t.CapacitySnapshot;
            var available = capacity - booked;

            decimal? segmentFare = null;
            if (farePolicy is not null && Fares.DistanceFareSupport.UsesDistanceFare(t))
            {
                var distanceKm = Fares.DistanceFareSupport.TryComputeSegmentDistanceKm(
                    t.Route.RouteStops, selectedSegment.Segment.FromOrder, selectedSegment.Segment.ToOrder);
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
                selectedSegment.FromStopDeparture, selectedSegment.ToStopArrival,
                Math.Max(0, available), capacity,
                minPrice, t.TripStatus.ToString(),
                IsBookingClosed: false,
                IsBookable: available > 0);
        })
        .Where(dto => dto is not null)
        .Select(dto => dto!)
        .ToList();
    }

    private sealed record SearchSegment(int FromOrder, int ToOrder);

    private sealed record ResolvedSearchSegment(
        SearchSegment Segment,
        DateTimeOffset? FromStopDeparture,
        DateTimeOffset? ToStopArrival);

    private static ResolvedSearchSegment? ResolveOpenSearchSegment(
        Trip trip,
        IReadOnlyList<SearchSegment> segments,
        IReadOnlyDictionary<Guid, Dictionary<int, (DateTimeOffset? Arrival, DateTimeOffset? Departure)>> tripStopsByTripId,
        DateTimeOffset bookingCutoff)
    {
        foreach (var segment in segments)
        {
            var (fromStopDeparture, toStopArrival) = ResolveSegmentTimes(trip, segment, tripStopsByTripId);
            if (fromStopDeparture is null || fromStopDeparture > bookingCutoff)
            {
                return new ResolvedSearchSegment(segment, fromStopDeparture, toStopArrival);
            }
        }

        return null;
    }

    private static (DateTimeOffset? FromStopDeparture, DateTimeOffset? ToStopArrival) ResolveSegmentTimes(
        Trip trip,
        SearchSegment segment,
        IReadOnlyDictionary<Guid, Dictionary<int, (DateTimeOffset? Arrival, DateTimeOffset? Departure)>> tripStopsByTripId)
    {
        if (tripStopsByTripId.TryGetValue(trip.Id, out var stopsByOrder))
        {
            var fromDeparture = stopsByOrder.TryGetValue(segment.FromOrder, out var fromStop)
                ? fromStop.Departure ?? fromStop.Arrival
                : null;
            var toArrival = stopsByOrder.TryGetValue(segment.ToOrder, out var toStop)
                ? toStop.Arrival ?? toStop.Departure
                : null;
            return (fromDeparture ?? trip.DepartureTime, toArrival ?? trip.ArrivalTime);
        }

        // Trip cũ chưa có trip_stops: suy lịch dừng từ route stops (cùng cách BuildStopDtos fallback).
        var drafts = TripStopScheduleSupport.BuildFromRouteStops(
            trip.Route.RouteStops.OrderBy(rs => rs.StopOrder).ToList(),
            trip.DepartureTime);
        var fromDraft = drafts.FirstOrDefault(d => d.StopOrder == segment.FromOrder);
        var toDraft = drafts.FirstOrDefault(d => d.StopOrder == segment.ToOrder);
        return (
            fromDraft?.PlannedDepartureTime ?? fromDraft?.PlannedArrivalTime ?? trip.DepartureTime,
            toDraft?.PlannedArrivalTime ?? toDraft?.PlannedDepartureTime ?? trip.ArrivalTime);
    }
}
