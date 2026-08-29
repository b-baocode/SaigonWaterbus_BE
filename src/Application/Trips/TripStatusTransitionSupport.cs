using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Application.Trips;

public sealed record TripStopDwellCountdownDto(
    Guid TripStopId,
    Guid StationId,
    string? StationCode,
    string? StationName,
    int StopOrder,
    int StayDurationMinutes,
    DateTimeOffset? StartedAt,
    DateTimeOffset EndsAt,
    int RemainingSeconds,
    int RemainingMinutes,
    bool IsOverdue);

public static class TripStatusTransitionSupport
{
    public static readonly TimeSpan BoardingLeadTime = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan StartMovementTolerance = TimeSpan.FromMinutes(2);

    public static DateTimeOffset? ResolveScheduledDepartureAt(Trip? trip, TripStop? currentStop = null) =>
        currentStop?.AdjustedDepartureTime
        ?? currentStop?.PlannedDepartureTime
        ?? trip?.AdjustedDepartureTime
        ?? trip?.DepartureTime;

    public static bool CanMarkBoarding(Trip? trip, TripStop? currentStop, DateTimeOffset now)
    {
        var scheduledDepartureAt = ResolveScheduledDepartureAt(trip, currentStop);
        return scheduledDepartureAt.HasValue
            && now >= scheduledDepartureAt.Value.Subtract(BoardingLeadTime);
    }

    public static bool CanMarkInProgressFromMovement(Trip? trip, DateTimeOffset now)
    {
        var scheduledDepartureAt = ResolveScheduledDepartureAt(trip);
        return scheduledDepartureAt.HasValue
            && now >= scheduledDepartureAt.Value.Subtract(StartMovementTolerance);
    }

    public static TripStopDwellCountdownDto? ResolveDwellCountdown(
        Trip? trip,
        TripStop? currentStop,
        DateTimeOffset now)
    {
        if (trip?.TripStatus is TripStatus.Completed or TripStatus.Cancelled
            || currentStop is null
            || currentStop.ActualDepartureTime.HasValue
            || !string.Equals(currentStop.StopStatus, TripStopStatuses.Arrived, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var scheduledDepartureAt = ResolveScheduledDepartureAt(trip, currentStop);
        var minimumStopDepartureAt = currentStop.ActualArrivalTime?.AddMinutes(currentStop.StayDurationMinutes);
        var dwellEndsAt = Max(scheduledDepartureAt, minimumStopDepartureAt);
        if (!dwellEndsAt.HasValue)
        {
            return null;
        }

        var remainingSeconds = Math.Max(0, (int)Math.Ceiling((dwellEndsAt.Value - now).TotalSeconds));
        return new TripStopDwellCountdownDto(
            currentStop.Id,
            currentStop.StationId,
            currentStop.Station?.StationCode,
            currentStop.Station?.StationName,
            currentStop.StopOrder,
            currentStop.StayDurationMinutes,
            currentStop.ActualArrivalTime,
            dwellEndsAt.Value,
            remainingSeconds,
            remainingSeconds == 0 ? 0 : (int)Math.Ceiling(remainingSeconds / 60m),
            now > dwellEndsAt.Value);
    }

    private static DateTimeOffset? Max(DateTimeOffset? left, DateTimeOffset? right)
    {
        if (!left.HasValue)
        {
            return right;
        }

        if (!right.HasValue)
        {
            return left;
        }

        return left.Value >= right.Value ? left : right;
    }
}
