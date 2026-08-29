using SaigonWaterbus.Domain.Constants;
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
