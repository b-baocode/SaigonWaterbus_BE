using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Application.Trips;

public static class TripCodeSupport
{
    public const string BusPrefix = "BB";
    public const string SightseeingPrefix = "BS";
    public const string BookingCharterPrefix = "BR";

    public static string BuildRegularOrSightseeingTripCode(
        Route route,
        DateOnly operatingDate,
        string suffix)
    {
        var prefix = ResolvePrefix(route.RouteType);
        return $"{prefix}-{operatingDate:yyyyMMdd}-{route.RouteCode}-{suffix}";
    }

    public static string BuildCharterBookingTripCode(
        Booking booking,
        int boatOrder) =>
        // Format: BR-yyyyMMdd-{BookingCode}-{order}
        // Ngày chạy thật nằm ở Trip.OperatingDate; tripCode gắn date prefix để
        // truy vết nhanh, nhưng hàm tái tạo tripCode (vd. khi tái dựng manifest)
        // vẫn lấy OperatingDate hiện tại của trip để tránh lệch.
        booking.DepartureDate.HasValue
            ? $"{BookingCharterPrefix}-{booking.DepartureDate.Value:yyyyMMdd}-{booking.BookingCode}-{boatOrder}"
            : $"{BookingCharterPrefix}-{booking.BookingCode}-{boatOrder}";

    private static string ResolvePrefix(string routeType) =>
        string.Equals(routeType, RouteTypes.SightseeingLoop, StringComparison.OrdinalIgnoreCase)
            ? SightseeingPrefix
            : BusPrefix;
}
