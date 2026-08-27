using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Application.Fares;
using SaigonWaterbus.Application.StaffWorkAssignments;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Application.Trips;

[Authorize(Roles = "Admin")]
public sealed record BatchCreateTripsCommand(
    DateOnly OperatingDate,
    List<BatchTripItem> Items) : IRequest<BatchCreateTripsResult>;

public sealed record BatchTripItem(
    string RouteCode,
    string BoatCode,
    List<TimeOnly> DepartureTimes,
    List<CreateTripStopScheduleInput>? Stops = null);

public sealed record BatchCreateTripsResult
{
    public int TotalCreated { get; init; }
    public int TotalSkipped { get; init; }
    public List<string> SkippedReasons { get; init; } = [];
    public List<TripDetailDto> Trips { get; init; } = [];
}

public sealed class BatchCreateTripsCommandValidator : AbstractValidator<BatchCreateTripsCommand>
{
    public BatchCreateTripsCommandValidator()
    {
        RuleFor(x => x.OperatingDate).NotEmpty();
        RuleFor(x => x.Items).NotEmpty();
        RuleForEach(x => x.Items).ChildRules(item =>
        {
            item.RuleFor(x => x.RouteCode).NotEmpty();
            item.RuleFor(x => x.BoatCode).NotEmpty();
            item.RuleFor(x => x.DepartureTimes).NotEmpty();
        });
    }
}

public sealed class BatchCreateTripsCommandHandler : IRequestHandler<BatchCreateTripsCommand, BatchCreateTripsResult>
{
    private static readonly TimeZoneInfo VietnamTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh");

    private readonly IApplicationDbContext _context;

    public BatchCreateTripsCommandHandler(IApplicationDbContext context) => _context = context;

    public async Task<BatchCreateTripsResult> Handle(BatchCreateTripsCommand request, CancellationToken cancellationToken)
    {
        var skipped = new List<string>();
        var trips = new List<TripDetailDto>();
        var seenTrips = new HashSet<(Guid RouteId, Guid BoatId, DateOnly Date, TimeOnly Time)>();

        foreach (var item in request.Items)
        {
            var route = await _context.Set<Route>()
                .Include(r => r.RouteStops.OrderBy(rs => rs.StopOrder))
                    .ThenInclude(rs => rs.Station)
                .AsNoTracking()
                .SingleOrDefaultAsync(r => r.RouteCode == item.RouteCode && r.Status == "Active", cancellationToken);

            if (route is null)
            {
                skipped.Add($"Route '{item.RouteCode}' not found or inactive.");
                continue;
            }

            if (route.RouteStops.Count < 2)
            {
                skipped.Add($"Route '{item.RouteCode}' has less than 2 stops.");
                continue;
            }

            var boat = await _context.Set<Boat>()
                .AsNoTracking()
                .SingleOrDefaultAsync(b => b.Code == item.BoatCode && b.Status == BoatStatus.Active, cancellationToken);

            if (boat is null)
            {
                skipped.Add($"Boat '{item.BoatCode}' not found or inactive.");
                continue;
            }

            if (!boat.SeatsConfigured || boat.SeatsConfigured == false)
            {
                skipped.Add($"Boat '{item.BoatCode}' has no configured seats.");
                continue;
            }

            var activeSeats = await _context.Set<Seat>()
                .Where(s => s.BoatId == boat.Id && s.IsActive)
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            if (activeSeats.Count <= 0)
            {
                skipped.Add($"Boat '{item.BoatCode}' has no active seats.");
                continue;
            }

            foreach (var time in item.DepartureTimes)
            {
                var key = (route.Id, boat.Id, request.OperatingDate, time);
                if (seenTrips.Contains(key))
                    continue;
                seenTrips.Add(key);

                try
                {
                    var trip = await CreateTripDirectlyAsync(
                        route, boat, request.OperatingDate, time,
                        activeSeats, item.Stops, cancellationToken);
                    trips.Add(trip);
                }
                catch (Exception ex)
                {
                    skipped.Add($"[{route.RouteCode}/{boat.Code} {time}] {ex.Message}");
                }
            }
        }

        return new BatchCreateTripsResult
        {
            TotalCreated = trips.Count,
            TotalSkipped = skipped.Count,
            SkippedReasons = skipped,
            Trips = trips
        };
    }

    private async Task<TripDetailDto> CreateTripDirectlyAsync(
        Route route,
        Boat boat,
        DateOnly operatingDate,
        TimeOnly time,
        List<Seat> activeSeats,
        List<CreateTripStopScheduleInput>? stopOverrides,
        CancellationToken cancellationToken)
    {
        var departureDateTime = new DateTime(
            operatingDate.Year,
            operatingDate.Month,
            operatingDate.Day,
            time.Hour,
            time.Minute,
            time.Second,
            DateTimeKind.Unspecified);

        var departureUtc = TimeZoneInfo.ConvertTimeToUtc(departureDateTime, VietnamTimeZone);

        var tripCode = TripCodeSupport.BuildRegularOrSightseeingTripCode(
            route,
            operatingDate,
            Random.Shared.Next(1000, 9999).ToString());

        var stayDurations = TripStopScheduleSupport.ResolveStayDurationMinutesByStopOrder(
            route, stopOverrides, "stops");

        var stopDrafts = TripStopScheduleSupport.BuildFromRouteStops(
            route.RouteStops,
            departureUtc,
            stayDurations,
            route.RouteType,
            route.EstimatedDurationMin);

        var arrivalUtc = stopDrafts[^1].PlannedArrivalTime ?? departureUtc;

        var capacity = activeSeats.Count;

        var trip = new Trip
        {
            RouteId = route.Id,
            BoatId = boat.Id,
            TripCode = tripCode,
            OperatingDate = operatingDate,
            DepartureTime = departureUtc,
            ArrivalTime = arrivalUtc,
            CapacitySnapshot = capacity,
            TripStatus = TripStatus.Scheduled
        };

        _context.Set<Trip>().Add(trip);

            foreach (var draft in stopDrafts)
        {
            var ts = new TripStop
            {
                TripId = trip.Id,
                StationId = draft.StationId,
                StopOrder = draft.StopOrder,
                PlannedArrivalTime = draft.PlannedArrivalTime,
                PlannedDepartureTime = draft.PlannedDepartureTime ?? draft.PlannedArrivalTime,
                StayDurationMinutes = draft.StayDurationMinutes,
                StopStatus = TripStopStatuses.Scheduled
            };
            _context.Set<TripStop>().Add(ts);
        }

        foreach (var seat in activeSeats)
        {
            _context.Set<TripSeat>().Add(new TripSeat
            {
                TripId = trip.Id,
                SeatId = seat.Id,
                Price = null
            });
        }

        await _context.SaveChangesAsync(cancellationToken);

        var onBoardStaff = await OnBoardStaffTripSupport.LoadTripOnBoardStaffAsync(
            _context, trip, DateTimeOffset.UtcNow, cancellationToken);

        return new TripDetailDto(
            trip.Id, trip.TripCode,
            route.Id, route.RouteName,
            route.RouteType,
            DistanceFareSupport.UsesDistanceFare(trip.TripType, route.RouteType),
            trip.DepartureTime, trip.ArrivalTime,
            trip.CapacitySnapshot, trip.TripStatus.ToString(), trip.StatusNote,
            TripStopScheduleSupport.BuildStopDtos(trip),
            Boat: TripMediaSupport.ToBoatDto(boat, trip.CapacitySnapshot),
            OnBoardStaff: onBoardStaff,
            RouteCode: route.RouteCode,
            FromStation: TripMediaSupport.ResolveFromStation(trip),
            ToStation: TripMediaSupport.ResolveToStation(trip),
            StopCount: stopDrafts.Count);
    }
}
