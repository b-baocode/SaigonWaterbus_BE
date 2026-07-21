using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Application.Trips;

internal static class TripBookingAvailabilitySupport
{
    public static readonly TripStatus[] CustomerBookableStatuses =
    [
        TripStatus.Scheduled,
        TripStatus.Boarding,
        TripStatus.InProgress,
        TripStatus.Delayed
    ];

    public static bool CanAcceptCustomerBooking(TripStatus status) =>
        status is TripStatus.Scheduled
            or TripStatus.Boarding
            or TripStatus.InProgress
            or TripStatus.Delayed;
}
