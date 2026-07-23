using SaigonWaterbus.Application.Bookings;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Application.StaffWorkAssignments;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;

namespace SaigonWaterbus.Application.Trips;

public sealed record GetTripDetailQuery(Guid TripId) : IRequest<TripDetailDto>;

public sealed class GetTripDetailQueryHandler : IRequestHandler<GetTripDetailQuery, TripDetailDto>
{
    private readonly IApplicationDbContext _context;
    private readonly TimeProvider _timeProvider;

    public GetTripDetailQueryHandler(IApplicationDbContext context, TimeProvider? timeProvider = null)
    {
        _context = context;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<TripDetailDto> Handle(GetTripDetailQuery request, CancellationToken cancellationToken)
    {
        var trip = await _context.Set<Trip>()
            .Include(t => t.Boat)
            .Include(t => t.Route)
                .ThenInclude(r => r.RouteStops)
                    .ThenInclude(rs => rs.Station)
            .Include(t => t.TripStops)
                .ThenInclude(ts => ts.Station)
            .SingleOrDefaultAsync(t => t.Id == request.TripId, cancellationToken)
            ?? throw new NotFoundException("Trip not found.");

        var sourceBooking = trip.SourceBookingId.HasValue
            ? await _context.Set<Booking>()
                .Include(x => x.ItineraryStops)
                .SingleOrDefaultAsync(x => x.Id == trip.SourceBookingId.Value, cancellationToken)
            : null;

        var now = _timeProvider.GetUtcNow();
        var assignments = await LoadTripAssignmentsAsync(trip, cancellationToken);
        var onBoardStaff = assignments
            .Where(x => x.AssignmentType == StaffWorkAssignmentType.Boat)
            .OrderBy(x => x.StartAt)
            .ThenBy(x => x.StaffUser.FullName)
            .Select(x => OnBoardStaffTripSupport.ToTripStaffDto(x, now))
            .ToList();
        var scanningStaffByTripStopId = assignments
            .Where(x => x.AssignmentType == StaffWorkAssignmentType.Station && x.TripStopId.HasValue);
        var scanningStaffByStop = trip.TripStops
            .ToDictionary(
                x => x.Id,
                _ => (IReadOnlyList<TripStaffAssignmentDto>)onBoardStaff);
        if (onBoardStaff.Count == 0)
        {
            scanningStaffByStop = scanningStaffByTripStopId
                .GroupBy(x => x.TripStopId!.Value)
                .ToDictionary(
                    g => g.Key,
                    g => (IReadOnlyList<TripStaffAssignmentDto>)g
                        .OrderBy(x => x.StartAt)
                        .ThenBy(x => x.StaffUser.FullName)
                        .Select(x => OnBoardStaffTripSupport.ToTripStaffDto(x, now))
                        .ToList());
        }
        var passengerCounts = await LoadPassengerCountsAsync(trip, now, cancellationToken);

        return UpdateTripStatusCommandHandler.ToDetailDto(
            trip,
            sourceBooking,
            TripStopScheduleSupport.BuildStopDtos(
                trip,
                scanningStaffByTripStopId: scanningStaffByStop,
                passengerCountsByTripStopId: passengerCounts.ByTripStopId),
            onBoardStaff,
            passengerCounts.TotalPassengerCount);
    }

    private async Task<IReadOnlyList<StaffWorkAssignment>> LoadTripAssignmentsAsync(
        Trip trip,
        CancellationToken cancellationToken)
    {
        var tripStopIds = trip.TripStops.Select(x => x.Id).ToList();
        return await _context.StaffWorkAssignments
            .AsNoTracking()
            .Include(x => x.StaffUser)
            .Where(x => x.Status != StaffWorkAssignmentStatus.Cancelled
                && x.Status != StaffWorkAssignmentStatus.Replaced
                && x.StartAt < trip.ArrivalTime
                && trip.DepartureTime < x.EndAt
                && ((x.AssignmentType == StaffWorkAssignmentType.Boat
                        && trip.BoatId.HasValue
                        && x.BoatId == trip.BoatId.Value)
                    || (x.AssignmentType == StaffWorkAssignmentType.Station
                        && x.TripStopId.HasValue
                        && tripStopIds.Contains(x.TripStopId.Value))))
            .ToListAsync(cancellationToken);
    }

    private async Task<TripPassengerCounts> LoadPassengerCountsAsync(
        Trip trip,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (trip.TripStops.Count == 0)
        {
            return new TripPassengerCounts(new Dictionary<Guid, TripStopPassengerCounts>(), 0);
        }

        var orderedStops = trip.TripStops
            .OrderBy(x => x.StopOrder)
            .ToList();
        var firstStopOrder = orderedStops.First().StopOrder;
        var lastStopOrder = orderedStops.Last().StopOrder;
        var tripStopByOrder = orderedStops.ToDictionary(x => x.StopOrder);
        var mutableCountsByOrder = orderedStops.ToDictionary(
            x => x.StopOrder,
            _ => new MutableTripStopPassengerCounts());
        var passengers = await _context.Set<BookingPassenger>()
            .AsNoTracking()
            .Where(x => x.TripId == trip.Id || (!x.TripId.HasValue && x.Booking.TripId == trip.Id))
            .Where(BookingSeatOccupancySupport.PassengerOccupiesSeat(now))
            .Select(x => new { x.FromStopOrder, x.ToStopOrder })
            .ToListAsync(cancellationToken);

        foreach (var passenger in passengers)
        {
            var fromStopOrder = Math.Max(passenger.FromStopOrder ?? firstStopOrder, firstStopOrder);
            var toStopOrder = Math.Min(passenger.ToStopOrder ?? lastStopOrder, lastStopOrder);

            if (toStopOrder <= fromStopOrder)
            {
                continue;
            }

            var boardingStopOrder = tripStopByOrder.ContainsKey(fromStopOrder)
                ? fromStopOrder
                : firstStopOrder;
            var alightingStopOrder = tripStopByOrder.ContainsKey(toStopOrder)
                ? toStopOrder
                : lastStopOrder;

            mutableCountsByOrder[boardingStopOrder].BoardingPassengerCount++;
            mutableCountsByOrder[alightingStopOrder].AlightingPassengerCount++;

            foreach (var stop in orderedStops.Where(x => x.StopOrder >= fromStopOrder && x.StopOrder < toStopOrder))
            {
                mutableCountsByOrder[stop.StopOrder].OnboardPassengerCount++;
                mutableCountsByOrder[stop.StopOrder].SegmentPassengerCount++;
            }
        }

        return new TripPassengerCounts(
            mutableCountsByOrder.ToDictionary(
                x => tripStopByOrder[x.Key].Id,
                x => x.Value.ToDto()),
            passengers.Count);
    }

    private sealed record TripPassengerCounts(
        IReadOnlyDictionary<Guid, TripStopPassengerCounts> ByTripStopId,
        int TotalPassengerCount);

    private sealed class MutableTripStopPassengerCounts
    {
        public int BoardingPassengerCount { get; set; }
        public int AlightingPassengerCount { get; set; }
        public int OnboardPassengerCount { get; set; }
        public int SegmentPassengerCount { get; set; }

        public TripStopPassengerCounts ToDto() => new(
            BoardingPassengerCount,
            AlightingPassengerCount,
            OnboardPassengerCount,
            SegmentPassengerCount);
    }

}
