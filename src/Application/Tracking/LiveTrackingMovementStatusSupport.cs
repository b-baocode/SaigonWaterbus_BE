using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Application.Tracking;

public static class LiveTrackingMovementStatusSupport
{
    public const string Moving = "Moving";
    public const string AtStation = "AtStation";
    public const string Completed = "Completed";
    public const string Cancelled = "Cancelled";
    private const decimal MovingSpeedThresholdKmh = 0.5m;

    public static string Resolve(
        Trip? trip,
        IReadOnlyCollection<TripStop> tripStops,
        string? gpsStatus,
        decimal? speedKmh,
        bool isGpsOnline,
        DateTimeOffset now)
    {
        if (trip?.TripStatus == TripStatus.Cancelled)
        {
            return Cancelled;
        }

        if (trip?.TripStatus == TripStatus.Completed)
        {
            return Completed;
        }

        if (tripStops.Any(stop =>
            string.Equals(stop.StopStatus, TripStopStatuses.Arrived, StringComparison.OrdinalIgnoreCase)
            && stop.ActualDepartureTime is null))
        {
            return AtStation;
        }

        if (tripStops.Any(stop =>
            string.Equals(stop.StopStatus, TripStopStatuses.Arriving, StringComparison.OrdinalIgnoreCase)))
        {
            return Moving;
        }

        if ((isGpsOnline && (IsMovingGpsStatus(gpsStatus) || speedKmh is > MovingSpeedThresholdKmh))
            || trip?.TripStatus == TripStatus.InProgress)
        {
            return Moving;
        }

        if (trip?.TripStatus == TripStatus.Boarding)
        {
            return TripStatus.Boarding.ToString();
        }

        if (trip?.TripStatus == TripStatus.Delayed)
        {
            return TripStatus.Delayed.ToString();
        }

        if (trip is not null && now < trip.DepartureTime)
        {
            return TripStatus.Scheduled.ToString();
        }

        return string.IsNullOrWhiteSpace(gpsStatus) ? "Unknown" : gpsStatus.Trim();
    }

    public static bool IsMovingGpsStatus(string? status) =>
        string.Equals(status, "moving", StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, "departed", StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, "in_progress", StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, "inprogress", StringComparison.OrdinalIgnoreCase);
}
