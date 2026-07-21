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
            .Select(x => ToStaffDto(x, now))
            .ToList();
        var scanningStaffByTripStopId = assignments
            .Where(x => x.AssignmentType == StaffWorkAssignmentType.Station && x.TripStopId.HasValue)
            .GroupBy(x => x.TripStopId!.Value)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<TripStaffAssignmentDto>)g
                    .OrderBy(x => x.StartAt)
                    .ThenBy(x => x.StaffUser.FullName)
                    .Select(x => ToStaffDto(x, now))
                    .ToList());
        var boardingCounts = await LoadBoardingPassengerCountsAsync(trip, now, cancellationToken);

        return UpdateTripStatusCommandHandler.ToDetailDto(
            trip,
            sourceBooking,
            TripStopScheduleSupport.BuildStopDtos(trip, boardingCounts, scanningStaffByTripStopId),
            onBoardStaff);
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

    private async Task<IReadOnlyDictionary<Guid, int>> LoadBoardingPassengerCountsAsync(
        Trip trip,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (trip.TripStops.Count == 0)
        {
            return new Dictionary<Guid, int>();
        }

        var firstStop = trip.TripStops.OrderBy(x => x.StopOrder).First();
        var tripStopIdByOrder = trip.TripStops.ToDictionary(x => x.StopOrder, x => x.Id);
        var passengers = await _context.Set<BookingPassenger>()
            .AsNoTracking()
            .Where(x => x.TripId == trip.Id || (!x.TripId.HasValue && x.Booking.TripId == trip.Id))
            .Where(BookingSeatOccupancySupport.PassengerOccupiesSeat(now))
            .Select(x => new { x.FromStopOrder })
            .ToListAsync(cancellationToken);

        return passengers
            .Select(x => tripStopIdByOrder.TryGetValue(x.FromStopOrder ?? firstStop.StopOrder, out var tripStopId)
                ? tripStopId
                : firstStop.Id)
            .GroupBy(x => x)
            .ToDictionary(x => x.Key, x => x.Count());
    }

    private static TripStaffAssignmentDto ToStaffDto(
        StaffWorkAssignment assignment,
        DateTimeOffset now) =>
        new(
            assignment.Id,
            assignment.StaffUserId,
            assignment.StaffUser.FullName,
            assignment.StaffUser.StaffType?.ToString(),
            assignment.AssignmentType.ToString(),
            assignment.StartAt,
            assignment.EndAt,
            assignment.Status.ToString(),
            StaffWorkAssignmentSupport.ResolveShiftState(assignment, now),
            assignment.DutyRole);
}
