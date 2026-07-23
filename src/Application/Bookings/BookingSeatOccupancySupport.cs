using System.Linq.Expressions;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Application.Bookings;

/// <summary>
/// Quy tắc chung xác định booking nào đang chiếm ghế của chuyến.
/// Booking PendingPayment chỉ giữ ghế trong thời hạn HoldExpiresAt (tối đa 15 phút);
/// quá hạn thì ghế được coi là trống trở lại (job nền sẽ chuyển booking sang Expired).
/// </summary>
public static class BookingSeatOccupancySupport
{
    /// <summary>Thời gian booking PendingPayment được giữ ghế trước khi hết hạn.</summary>
    public static readonly TimeSpan BookingHoldDuration = TimeSpan.FromMinutes(15);

    /// <summary>Thời gian tạm giữ ghế khi khách đang chọn trên sơ đồ (pre-hold).</summary>
    public static readonly TimeSpan SeatPreHoldDuration = TimeSpan.FromMinutes(3);

    public static Expression<Func<Booking, bool>> BookingOccupiesSeats(DateTimeOffset now) =>
        b => b.BookingStatus != BookingStatus.Cancelled
          && b.BookingStatus != BookingStatus.Expired
          && b.BookingStatus != BookingStatus.Refunded
          && (b.BookingStatus != BookingStatus.PendingPayment
              || b.HoldExpiresAt == null
              || b.HoldExpiresAt > now);

    public static Expression<Func<BookingPassenger, bool>> PassengerOccupiesSeat(DateTimeOffset now) =>
        p => p.Booking.BookingStatus != BookingStatus.Cancelled
          && p.Booking.BookingStatus != BookingStatus.Expired
          && p.Booking.BookingStatus != BookingStatus.Refunded
          && (p.Booking.BookingStatus != BookingStatus.PendingPayment
              || p.Booking.HoldExpiresAt == null
              || p.Booking.HoldExpiresAt > now);

    /// <summary>
    /// Hai chặng [aFrom, aTo) và [bFrom, bTo) (nửa mở theo stop_order — khách xuống trạm nào
    /// thì ghế trống từ trạm đó) có giao nhau hay không.
    /// </summary>
    public static bool SegmentsOverlap(int aFrom, int aTo, int bFrom, int bTo) =>
        aFrom < bTo && bFrom < aTo;

    /// <summary>Chặng quy ước "cả trip" cho dữ liệu cũ / trip sightseeing (không bán ghế theo chặng).</summary>
    public const int FullTripFromOrder = int.MinValue;
    public const int FullTripToOrder = int.MaxValue;

    /// <summary>
    /// Passenger có chiếm ghế trên chặng [fromOrder, toOrder) không. Passenger không có
    /// stop order (dữ liệu cũ, sightseeing) được coi là chiếm ghế cả trip.
    /// </summary>
    public static Expression<Func<BookingPassenger, bool>> PassengerOverlapsSegment(int fromOrder, int toOrder) =>
        p => (p.FromStopOrder ?? int.MinValue) < toOrder
          && fromOrder < (p.ToStopOrder ?? int.MaxValue);

    /// <summary>Booking PendingPayment đã quá hạn giữ chỗ hay chưa.</summary>
    public static bool IsHoldExpired(Booking booking, DateTimeOffset now) =>
        booking.BookingStatus == BookingStatus.PendingPayment
        && !Booking.IsCharterBookingType(booking.BookingType)
        && booking.HoldExpiresAt.HasValue
        && booking.HoldExpiresAt.Value <= now;
}
