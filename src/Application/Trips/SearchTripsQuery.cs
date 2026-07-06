using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Application.Seats;
using SaigonWaterbus.Application.TicketTypes;
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
                .ThenInclude(r => r.RouteStops)
            .Where(t => routeIds.Contains(t.RouteId)
                     && t.OperatingDate == request.OperatingDate
                     && t.TripStatus == TripStatus.Scheduled
                     && t.DepartureTime > now)
            .ToListAsync(cancellationToken);

        var tripIds = trips.Select(t => t.Id).ToList();

        var bookedCounts = await _context.Set<Booking>()
            .Where(b => b.TripId.HasValue
                     && tripIds.Contains(b.TripId.Value)
                     && b.BookingStatus != BookingStatus.Cancelled
                     && b.BookingStatus != BookingStatus.Expired
                     && b.BookingStatus != BookingStatus.Refunded)
            .Select(b => new { TripId = b.TripId!.Value, Count = b.Passengers.Count })
            .GroupBy(x => x.TripId)
            .Select(g => new { TripId = g.Key, Count = g.Sum(x => x.Count) })
            .ToDictionaryAsync(x => x.TripId, x => x.Count, cancellationToken);

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
        var minModifier = TicketTypePricing.All.Min(x => x.PriceModifier);

        return trips.OrderBy(t => t.DepartureTime).Select(t =>
        {
            var booked = bookedCounts.GetValueOrDefault(t.Id, 0);
            seatStats.TryGetValue(t.BoatId ?? Guid.Empty, out var stats);
            var capacity = stats?.ActiveSeatCount ?? t.CapacitySnapshot;
            var available = capacity - booked;
            var minPrice = stats?.MinSeatPrice is > 0
                ? stats.MinSeatPrice.Value * minModifier
                : (decimal?)null;

            return new TripSummaryDto(
                t.Id, t.TripCode, t.Route.RouteName,
                t.DepartureTime, t.ArrivalTime,
                t.DepartureTime, t.ArrivalTime,
                Math.Max(0, available), capacity,
                minPrice, t.TripStatus.ToString());
        }).ToList();
    }
}
