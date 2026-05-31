using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Application.Trips;

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

        // Tìm routes có cả from_station và to_station, đúng thứ tự
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

        var trips = await _context.Set<Trip>()
            .Include(t => t.Route)
            .Include(t => t.Boat)
            .Include(t => t.TripStops)
            .Where(t => routeIds.Contains(t.RouteId)
                     && t.OperatingDate == request.OperatingDate
                     && t.TripStatus == TripStatus.Scheduled
                     && t.DepartureTime > now)
            .ToListAsync(cancellationToken);

        // Tính available seats
        var tripIds = trips.Select(t => t.Id).ToList();

        var heldCounts = await _context.Set<SeatHold>()
            .Where(sh => tripIds.Contains(sh.TripId)
                      && sh.HoldStatus == SeatHoldStatus.Active
                      && sh.ExpiresAt > now)
            .GroupBy(sh => sh.TripId)
            .Select(g => new { TripId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.TripId, x => x.Count, cancellationToken);

        var bookedCounts = await _context.Set<BookingItem>()
            .Where(bi => tripIds.Contains(bi.TripId) && bi.ItemStatus != BookingItemStatus.Cancelled)
            .GroupBy(bi => bi.TripId)
            .Select(g => new { TripId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.TripId, x => x.Count, cancellationToken);

        // Tính min price từ FareMatrix
        var routeFromTo = validRouteIds.ToDictionary(x => x.RouteId, x => x);

        var farePrices = await _context.Set<FareMatrix>()
            .Where(f => routeIds.Contains(f.RouteId)
                     && f.FromStationId == request.FromStationId
                     && f.ToStationId == request.ToStationId
                     && f.IsActive)
            .ToDictionaryAsync(f => f.RouteId, f => f.BasePrice, cancellationToken);

        var minModifier = await _context.Set<TicketType>()
            .Where(tt => tt.IsActive)
            .MinAsync(tt => (decimal?)tt.PriceModifier, cancellationToken) ?? 1m;

        return trips.OrderBy(t => t.DepartureTime).Select(t =>
        {
            var routeStops = routeFromTo.TryGetValue(t.RouteId, out var rs) ? rs : null;
            var fromTripStop = routeStops != null
                ? t.TripStops.FirstOrDefault(ts => ts.RouteStopId == routeStops.FromStop.Id)
                : null;
            var toTripStop = routeStops != null
                ? t.TripStops.FirstOrDefault(ts => ts.RouteStopId == routeStops.ToStop.Id)
                : null;

            var held = heldCounts.GetValueOrDefault(t.Id, 0);
            var booked = bookedCounts.GetValueOrDefault(t.Id, 0);
            var available = t.CapacitySnapshot - held - booked;

            farePrices.TryGetValue(t.RouteId, out var basePrice);
            var minPrice = basePrice > 0 ? (decimal?)(basePrice * minModifier) : null;

            return new TripSummaryDto(
                t.Id, t.TripCode, t.Route.RouteName, t.Boat.BoatName,
                t.DepartureTime, t.ArrivalTime,
                fromTripStop?.ScheduledDeparture, toTripStop?.ScheduledArrival,
                Math.Max(0, available), t.CapacitySnapshot,
                minPrice, t.TripStatus.ToString());
        }).ToList();
    }
}
