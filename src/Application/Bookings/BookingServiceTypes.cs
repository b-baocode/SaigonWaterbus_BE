using SaigonWaterbus.Domain.Constants;

namespace SaigonWaterbus.Application.Bookings;

/// <summary>
/// Dịch vụ mà booking thuộc về, suy từ routeType của chuyến — để FE render đúng màn hình
/// (vé waterbus có chặng đi/đến, tour ngắm cảnh đi nguyên chuyến). Khác với
/// <c>Booking.BookingType</c> (SeatBooking/CharterBooking) là cách lưu đơn trong DB.
/// </summary>
public static class BookingServiceTypes
{
    public const string Waterbus = "Waterbus";
    public const string Sightseeing = "Sightseeing";
    public const string Charter = "Charter";

    public static string Resolve(string? routeType)
    {
        if (string.Equals(routeType, RouteTypes.SightseeingLoop, StringComparison.OrdinalIgnoreCase))
        {
            return Sightseeing;
        }

        if (string.Equals(routeType, RouteTypes.Charter, StringComparison.OrdinalIgnoreCase))
        {
            return Charter;
        }

        return Waterbus;
    }
}
