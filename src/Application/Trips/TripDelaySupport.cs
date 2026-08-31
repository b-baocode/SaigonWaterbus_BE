using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Application.Tickets;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Application.Trips;

public static class TripDelaySupport
{
    public static int TurnaroundBufferMinutes =>
        (int)TripScheduleSupport.BoatTurnaroundBuffer.TotalMinutes;

    public static TripDelayInfoDto? ToDelayInfoDto(Trip trip)
    {
        if (trip.DelayMinutes <= 0
            && !trip.DelayStartedAt.HasValue
            && !trip.DelayEndedAt.HasValue
            && !trip.DelayStartStopOrder.HasValue
            && trip.DelayPropagationMinutes <= 0
            && string.IsNullOrWhiteSpace(trip.DelayReason))
        {
            return null;
        }

        return new TripDelayInfoDto(
            trip.DelayMinutes,
            trip.DelayReason,
            trip.DelayStartedAt.HasValue && !trip.DelayEndedAt.HasValue,
            trip.DelayStartedAt,
            trip.DelayEndedAt,
            trip.DelayStartStopOrder,
            trip.DelayPropagationMinutes);
    }

    public static int ResolveDelayStartStopOrder(Trip trip)
    {
        var tripStops = trip.TripStops.OrderBy(x => x.StopOrder).ToList();
        if (tripStops.Count == 0)
        {
            return trip.Route.RouteStops
                .OrderBy(x => x.StopOrder)
                .Select(x => x.StopOrder)
                .DefaultIfEmpty(1)
                .First();
        }

        var atStation = tripStops.FirstOrDefault(x =>
            string.Equals(x.StopStatus, TripStopStatuses.Arrived, StringComparison.OrdinalIgnoreCase)
            && !x.ActualDepartureTime.HasValue);
        if (atStation is not null)
        {
            return atStation.StopOrder;
        }

        var nextNotArrived = tripStops.FirstOrDefault(x => !x.ActualArrivalTime.HasValue);
        if (nextNotArrived is not null)
        {
            return nextNotArrived.StopOrder;
        }

        return tripStops
            .Where(x => !x.ActualDepartureTime.HasValue)
            .Select(x => x.StopOrder)
            .DefaultIfEmpty(tripStops.Max(x => x.StopOrder))
            .Min();
    }

    public static void ApplyDelayToTrip(
        Trip trip,
        int delayMinutes,
        string? reason,
        int startStopOrder)
    {
        trip.DelayMinutes = delayMinutes;
        trip.DelayReason = delayMinutes > 0 ? reason : null;

        if (trip.TripStops.Count == 0)
        {
            var firstRouteStopOrder = trip.Route.RouteStops
                .Select(x => x.StopOrder)
                .DefaultIfEmpty(1)
                .Min();
            trip.AdjustedDepartureTime = delayMinutes > 0 && startStopOrder <= firstRouteStopOrder
                ? trip.DepartureTime.AddMinutes(delayMinutes)
                : trip.AdjustedDepartureTime;
            trip.AdjustedArrivalTime = delayMinutes > 0
                ? trip.ArrivalTime.AddMinutes(delayMinutes)
                : null;
            return;
        }

        foreach (var stop in trip.TripStops.Where(x => x.StopOrder >= startStopOrder))
        {
            if (stop.ActualDepartureTime.HasValue)
            {
                continue;
            }

            stop.AdjustedArrivalTime = delayMinutes > 0
                ? stop.PlannedArrivalTime?.AddMinutes(delayMinutes)
                : null;
            stop.AdjustedDepartureTime = delayMinutes > 0
                ? stop.PlannedDepartureTime?.AddMinutes(delayMinutes)
                : null;
        }

        var orderedStops = trip.TripStops.OrderBy(x => x.StopOrder).ToList();
        var firstStop = orderedStops.FirstOrDefault();
        var lastStop = orderedStops.LastOrDefault();
        trip.AdjustedDepartureTime = firstStop?.AdjustedDepartureTime;
        trip.AdjustedArrivalTime = lastStop?.AdjustedArrivalTime
            ?? lastStop?.AdjustedDepartureTime
            ?? (delayMinutes > 0 ? trip.ArrivalTime.AddMinutes(delayMinutes) : null);
    }

    public static int CalculateCascadedTotalDelayMinutes(Trip futureTrip, DateTimeOffset previousBoatAvailableAt)
    {
        var earliestDeparture = previousBoatAvailableAt.AddMinutes(TurnaroundBufferMinutes);
        var currentAdjustedDeparture = ResolveAdjustedDeparture(futureTrip);
        var requiredDeparture = currentAdjustedDeparture >= earliestDeparture
            ? currentAdjustedDeparture
            : earliestDeparture;

        return Math.Max(
            futureTrip.DelayMinutes,
            Math.Max(0, (int)Math.Ceiling((requiredDeparture - futureTrip.DepartureTime).TotalMinutes)));
    }

    public static void ApplyTotalDelayToFutureTrip(Trip trip, int totalDelayMinutes, string reason)
    {
        if (totalDelayMinutes <= trip.DelayMinutes)
        {
            return;
        }

        trip.TripStatus = TripStatus.Delayed;
        trip.DelayMinutes = totalDelayMinutes;
        trip.DelayReason = string.IsNullOrWhiteSpace(trip.DelayReason)
            ? reason
            : trip.DelayReason.Contains(reason, StringComparison.Ordinal)
                ? trip.DelayReason
                : $"{trip.DelayReason.Trim()} {reason}";
        trip.AdjustedDepartureTime = trip.DepartureTime.AddMinutes(totalDelayMinutes);
        trip.AdjustedArrivalTime = trip.ArrivalTime.AddMinutes(totalDelayMinutes);

        foreach (var stop in trip.TripStops)
        {
            if (stop.ActualDepartureTime.HasValue)
            {
                continue;
            }

            stop.AdjustedArrivalTime = stop.PlannedArrivalTime?.AddMinutes(totalDelayMinutes);
            stop.AdjustedDepartureTime = stop.PlannedDepartureTime?.AddMinutes(totalDelayMinutes);
        }
    }

    public static async Task ExtendCoveringBoatAssignmentsAsync(
        IApplicationDbContext context,
        IEnumerable<Trip> trips,
        CancellationToken cancellationToken)
    {
        var delayedTrips = trips
            .Where(x => x.BoatId.HasValue)
            .Select(x => new
            {
                Trip = x,
                BoatId = x.BoatId!.Value,
                RequiredEndAt = ResolveAssignmentOperationalEnd(x)
            })
            .Where(x => x.RequiredEndAt > x.Trip.ArrivalTime)
            .ToList();
        if (delayedTrips.Count == 0)
        {
            return;
        }

        var boatIds = delayedTrips.Select(x => x.BoatId).Distinct().ToArray();
        var earliestDeparture = delayedTrips.Min(x => x.Trip.DepartureTime);
        var latestPlannedArrival = delayedTrips.Max(x => x.Trip.ArrivalTime);
        var assignments = await context.StaffWorkAssignments
            .Where(x => x.AssignmentType == StaffWorkAssignmentType.Boat
                && x.BoatId.HasValue
                && boatIds.Contains(x.BoatId.Value)
                && x.Status != StaffWorkAssignmentStatus.Cancelled
                && x.Status != StaffWorkAssignmentStatus.Replaced
                && x.StartAt <= latestPlannedArrival
                && x.EndAt >= earliestDeparture)
            .ToListAsync(cancellationToken);

        foreach (var assignment in assignments)
        {
            var requiredEndAt = delayedTrips
                .Where(x => x.BoatId == assignment.BoatId
                    && assignment.StartAt <= x.Trip.DepartureTime
                    && assignment.EndAt >= x.Trip.ArrivalTime)
                .Select(x => (DateTimeOffset?)x.RequiredEndAt)
                .Max();
            if (requiredEndAt.HasValue && assignment.EndAt < requiredEndAt.Value)
            {
                assignment.EndAt = requiredEndAt.Value;
            }
        }
    }

    public static DateTimeOffset ResolveAdjustedDeparture(Trip trip) =>
        trip.AdjustedDepartureTime
            ?? (trip.DelayMinutes > 0
                ? trip.DepartureTime.AddMinutes(trip.DelayMinutes)
                : trip.DepartureTime);

    public static DateTimeOffset ResolveAdjustedArrival(Trip trip) =>
        trip.AdjustedArrivalTime
            ?? (trip.DelayMinutes > 0
                ? trip.ArrivalTime.AddMinutes(trip.DelayMinutes)
                : trip.ArrivalTime);

    internal static DateTimeOffset ResolveAssignmentOperationalEnd(Trip trip)
    {
        var operationalEnd = ResolveAdjustedArrival(trip);
        foreach (var stop in trip.TripStops)
        {
            var arrival = stop.ActualArrivalTime
                ?? stop.AdjustedArrivalTime
                ?? stop.PlannedArrivalTime;
            if (arrival.HasValue)
            {
                var dwellMinutes = stop.StayDurationMinutes > 0
                    ? stop.StayDurationMinutes
                    : TicketAttendanceWindowSupport.UnscheduledDwellFallbackMinutes;
                operationalEnd = Max(operationalEnd, arrival.Value.AddMinutes(dwellMinutes));
            }

            var departure = stop.ActualDepartureTime
                ?? stop.AdjustedDepartureTime
                ?? stop.PlannedDepartureTime;
            if (departure.HasValue)
            {
                operationalEnd = Max(operationalEnd, departure.Value);
            }
        }

        return operationalEnd.AddMinutes(TicketAttendanceWindowSupport.CheckOutGraceMinutes);
    }

    private static DateTimeOffset Max(DateTimeOffset left, DateTimeOffset right) =>
        left >= right ? left : right;

    public static TripStatus ResolveResumedStatus(Trip trip, DateTimeOffset now)
    {
        if (trip.TripStatus is TripStatus.Completed or TripStatus.Cancelled)
        {
            return trip.TripStatus;
        }

        if (trip.TripStops.Any(x => x.ActualDepartureTime.HasValue)
            || now >= (trip.AdjustedDepartureTime ?? trip.DepartureTime))
        {
            return TripStatus.InProgress;
        }

        return TripStatusTransitionSupport.CanMarkBoarding(trip, CurrentStopOrFirst(trip), now)
            ? TripStatus.Boarding
            : TripStatus.Scheduled;
    }

    private static TripStop? CurrentStopOrFirst(Trip trip) =>
        trip.TripStops
            .OrderBy(x => x.StopOrder)
            .FirstOrDefault(x => !x.ActualDepartureTime.HasValue)
        ?? trip.TripStops.OrderBy(x => x.StopOrder).FirstOrDefault();
}
