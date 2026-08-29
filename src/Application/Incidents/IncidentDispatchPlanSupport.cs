using FluentValidation.Results;
using SaigonWaterbus.Application.Boats;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Application.Trips;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.Incidents;

internal static class IncidentDispatchPlanSupport
{
    public static IQueryable<Trip> BuildNextTripsQuery(
        IApplicationDbContext context,
        Incident incident)
    {
        var afterDeparture = incident.Trip?.DepartureTime ?? incident.OccurredAt;

        return context.Set<Trip>()
            .Where(x => x.BoatId == incident.BoatId
                && x.DepartureTime > afterDeparture
                && x.TripStatus != TripStatus.Completed
                && x.TripStatus != TripStatus.Cancelled
                && x.TripStatus != TripStatus.InProgress);
    }

    public static Task<List<Trip>> LoadNextTripsAsync(
        IApplicationDbContext context,
        Incident incident,
        bool asNoTracking,
        CancellationToken cancellationToken)
    {
        var query = BuildNextTripsQuery(context, incident)
            .Include(x => x.Route)
                .ThenInclude(x => x.RouteStops)
            .Include(x => x.TripStops)
            .Include(x => x.TripSeats)
                .ThenInclude(x => x.Seat);

        return (asNoTracking ? query.AsNoTracking() : query)
            .OrderBy(x => x.DepartureTime)
            .ThenBy(x => x.Id)
            .ToListAsync(cancellationToken);
    }

    public static async Task EnsureReplacementBoatEligibleAsync(
        IApplicationDbContext context,
        Boat replacementBoat,
        IReadOnlyList<Trip> nextTrips,
        CancellationToken cancellationToken)
    {
        if (replacementBoat.ServiceType != BoatServiceType.Passenger
            || replacementBoat.Status != BoatStatus.Active
            || !BoatSupport.IsReadyForOperation(replacementBoat))
        {
            throw InvalidBoat("Tàu thay thế phải là Passenger, Active và đã setup đủ ghế.");
        }

        if (nextTrips.Count == 0)
        {
            return;
        }

        var activeSeats = replacementBoat.Seats.Where(x => x.IsActive).ToList();
        var availableSeatCount = replacementBoat.Seats.Any()
            ? activeSeats.Count
            : replacementBoat.SeatCount;
        var requiredCapacity = nextTrips.Max(x => x.CapacitySnapshot);
        if (availableSeatCount < requiredCapacity)
        {
            throw InvalidBoat($"Tàu thay thế không đủ sức chứa cho các chuyến kế tiếp. Cần tối thiểu {requiredCapacity} ghế.");
        }

        var incompatibleTrip = nextTrips.FirstOrDefault(x =>
            !BoatRouteCompatibilitySupport.IsCompatible(x.Route.RouteType, replacementBoat.SeatSetupType));
        if (incompatibleTrip is not null)
        {
            throw InvalidBoat(
                $"Tàu thay thế không tương thích tuyến của chuyến {incompatibleTrip.TripCode}. "
                + BoatRouteCompatibilitySupport.BuildIncompatibleMessage(
                    incompatibleTrip.Route.RouteType,
                    replacementBoat.SeatSetupType));
        }

        var nextTripIds = nextTrips.Select(x => x.Id).ToArray();
        var passengerSeatCodes = await context.Set<BookingPassenger>()
            .AsNoTracking()
            .Where(x => x.TripId.HasValue
                && nextTripIds.Contains(x.TripId.Value)
                && x.TripSeatId.HasValue)
            .Select(x => x.TripSeat!.Seat.Code)
            .Distinct()
            .ToListAsync(cancellationToken);
        var activeSeatCodes = activeSeats
            .Select(x => x.Code)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missingSeatCodes = nextTrips
            .SelectMany(x => x.TripSeats)
            .Where(x => x.Status != TripSeat.StatusAvailable)
            .Select(x => x.Seat.Code)
            .Concat(passengerSeatCodes)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(x => !activeSeatCodes.Contains(x))
            .OrderBy(x => x)
            .ToArray();
        if (missingSeatCodes.Length > 0)
        {
            throw InvalidBoat($"Tàu thay thế thiếu các mã ghế đã được giữ/đặt: {string.Join(", ", missingSeatCodes)}.");
        }

        var windowStart = nextTrips.Min(ResolveDeparture).AddHours(-24);
        var windowEnd = nextTrips.Max(ResolveArrival).AddHours(24);
        var existingTrips = await context.Set<Trip>()
            .AsNoTracking()
            .Include(x => x.Route)
                .ThenInclude(x => x.RouteStops)
            .Where(x => x.BoatId == replacementBoat.Id
                && !nextTripIds.Contains(x.Id)
                && x.TripStatus != TripStatus.Completed
                && x.TripStatus != TripStatus.Cancelled
                && x.ArrivalTime >= windowStart
                && x.DepartureTime <= windowEnd)
            .OrderBy(x => x.DepartureTime)
            .ToListAsync(cancellationToken);

        foreach (var nextTrip in nextTrips)
        {
            var requestedDeparture = ResolveDeparture(nextTrip);
            var requestedArrival = ResolveArrival(nextTrip);
            var requestedStops = nextTrip.Route.RouteStops.OrderBy(x => x.StopOrder).ToList();
            if (requestedStops.Count >= 2)
            {
                var requestedWindow = new TripScheduleSupport.BoatScheduleWindow(
                    nextTrip.TripCode,
                    requestedDeparture,
                    requestedArrival,
                    TripScheduleSupport.ResolveStartStationId(requestedStops),
                    TripScheduleSupport.ResolveEndStationId(requestedStops),
                    requestedStops);
                var existingWindows = existingTrips
                    .Where(x => x.Route.RouteStops.Count >= 2)
                    .Select(x =>
                    {
                        var stops = x.Route.RouteStops.OrderBy(stop => stop.StopOrder).ToList();
                        return new TripScheduleSupport.BoatScheduleWindow(
                            x.TripCode,
                            ResolveDeparture(x),
                            ResolveArrival(x),
                            TripScheduleSupport.ResolveStartStationId(stops),
                            TripScheduleSupport.ResolveEndStationId(stops),
                            stops);
                    })
                    .ToList();
                var locationAwareConflict = TripScheduleSupport.FindConflict(requestedWindow, existingWindows);
                if (locationAwareConflict is not null)
                {
                    throw InvalidBoat(
                        $"Tàu thay thế trùng lịch với chuyến {locationAwareConflict.Existing.TripCode} "
                        + $"khi thay cho chuyến {nextTrip.TripCode}. "
                        + TripScheduleSupport.BuildLocationAwareConflictMessage(
                            locationAwareConflict.Existing.TripCode,
                            locationAwareConflict.Existing.DepartureTime,
                            locationAwareConflict.Existing.ArrivalTime,
                            locationAwareConflict.EarliestAllowedDeparture,
                            locationAwareConflict.RepositionDuration));
                }
            }

            var simpleConflict = existingTrips.FirstOrDefault(x =>
                (requestedStops.Count < 2 || x.Route.RouteStops.Count < 2)
                && TripScheduleSupport.ConflictsWithBuffer(
                    ResolveDeparture(x),
                    ResolveArrival(x),
                    requestedDeparture,
                    requestedArrival));
            if (simpleConflict is not null)
            {
                throw InvalidBoat(
                    $"Tàu thay thế trùng lịch với chuyến {simpleConflict.TripCode} "
                    + $"khi thay cho chuyến {nextTrip.TripCode}. "
                    + TripScheduleSupport.BuildConflictMessage(
                        simpleConflict.TripCode,
                        ResolveDeparture(simpleConflict),
                        ResolveArrival(simpleConflict)));
            }
        }
    }

    public static async Task AssignNextTripsAsync(
        IApplicationDbContext context,
        IReadOnlyList<Trip> nextTrips,
        Boat replacementBoat,
        string note,
        CancellationToken cancellationToken)
    {
        if (nextTrips.Count == 0)
        {
            return;
        }

        var replacementSeats = replacementBoat.Seats
            .Where(x => x.IsActive)
            .OrderBy(x => x.Deck)
            .ThenBy(x => x.Row)
            .ThenBy(x => x.Column)
            .ToList();
        if (replacementSeats.Count == 0 && nextTrips.Any(x => x.TripSeats.Count > 0))
        {
            throw InvalidBoat("Tàu thay thế chưa có dữ liệu ghế để chuyển các ghế của chuyến kế tiếp.");
        }
        var nextTripIds = nextTrips.Select(x => x.Id).ToArray();
        var passengers = await context.Set<BookingPassenger>()
            .Include(x => x.TripSeat)
                .ThenInclude(x => x!.Seat)
            .Where(x => x.TripId.HasValue
                && nextTripIds.Contains(x.TripId.Value)
                && x.TripSeatId.HasValue)
            .ToListAsync(cancellationToken);

        foreach (var trip in nextTrips)
        {
            var oldTripSeats = trip.TripSeats.ToList();
            if (oldTripSeats.Count == 0 && replacementSeats.Count == 0)
            {
                trip.BoatId = replacementBoat.Id;
                trip.Boat = replacementBoat;
                trip.CapacitySnapshot = replacementBoat.SeatCount;
                trip.StatusNote = note;
                continue;
            }
            var oldTripSeatsByCode = oldTripSeats
                .GroupBy(x => x.Seat.Code, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);
            var newTripSeats = replacementSeats.Select(seat =>
            {
                oldTripSeatsByCode.TryGetValue(seat.Code, out var oldTripSeat);
                return new TripSeat
                {
                    TripId = trip.Id,
                    SeatId = seat.Id,
                    Seat = seat,
                    Status = oldTripSeat?.Status ?? TripSeat.StatusAvailable,
                    Price = oldTripSeat?.Price
                };
            }).ToList();
            var newTripSeatsByCode = newTripSeats.ToDictionary(
                x => x.Seat.Code,
                StringComparer.OrdinalIgnoreCase);

            foreach (var passenger in passengers.Where(x => x.TripId == trip.Id))
            {
                var oldSeatCode = passenger.TripSeat?.Seat.Code;
                if (oldSeatCode is not null && newTripSeatsByCode.TryGetValue(oldSeatCode, out var newTripSeat))
                {
                    passenger.TripSeatId = newTripSeat.Id;
                    passenger.TripSeat = newTripSeat;
                }
            }

            context.Set<TripSeat>().AddRange(newTripSeats);
            context.Set<TripSeat>().RemoveRange(oldTripSeats);
            trip.BoatId = replacementBoat.Id;
            trip.Boat = replacementBoat;
            trip.CapacitySnapshot = replacementSeats.Count;
            trip.StatusNote = note;
        }
    }

    public static DateTimeOffset ResolveDeparture(Trip trip) =>
        trip.AdjustedDepartureTime ?? trip.DepartureTime;

    public static DateTimeOffset ResolveArrival(Trip trip) =>
        trip.AdjustedArrivalTime ?? trip.ArrivalTime;

    private static ValidationException InvalidBoat(string message) =>
        new([new ValidationFailure(nameof(AssignReplacementBoatCommand.ReplacementBoatId), message)]);
}
