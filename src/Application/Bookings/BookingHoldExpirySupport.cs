using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Application.Bookings;

/// <summary>
/// Chuyển booking thường PendingPayment quá hạn giữ chỗ sang Expired,
/// hoàn lượt khuyến mãi và phát sự kiện nhả ghế realtime.
/// </summary>
public static class BookingHoldExpirySupport
{
    /// <summary>Expire một booking cụ thể (dùng khi khách cố thanh toán booking đã quá hạn).</summary>
    public static async Task ExpireBookingAsync(
        IApplicationDbContext context,
        ITripSeatNotifier notifier,
        Booking booking,
        CancellationToken cancellationToken)
    {
        // Lượt khuyến mãi suy ra từ bookings — Expired tự nhả lượt, không cần bookkeeping.
        booking.BookingStatus = BookingStatus.Expired;

        await context.SaveChangesAsync(cancellationToken);
        await NotifySeatsReleasedAsync(context, notifier, booking, cancellationToken);
    }

    /// <summary>Quét và expire toàn bộ booking thường quá hạn giữ chỗ. Trả về số booking đã expire.</summary>
    public static async Task<int> ExpireOverdueBookingsAsync(
        IApplicationDbContext context,
        ITripSeatNotifier notifier,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var overdueBookings = await context.Set<Booking>()
            .Include(b => b.Passengers)
                .ThenInclude(p => p.TripSeat)
                    .ThenInclude(ts => ts!.Seat)
            .Where(b => b.BookingType == Booking.SeatBookingType
                     && b.BookingStatus == BookingStatus.PendingPayment
                     && b.HoldExpiresAt != null
                     && b.HoldExpiresAt <= now)
            .ToListAsync(cancellationToken);

        if (overdueBookings.Count == 0)
        {
            return 0;
        }

        foreach (var booking in overdueBookings)
        {
            booking.BookingStatus = BookingStatus.Expired;
        }

        await context.SaveChangesAsync(cancellationToken);

        foreach (var tripGroup in overdueBookings
            .Where(b => b.TripId.HasValue)
            .GroupBy(b => b.TripId!.Value))
        {
            var releasedSeats = tripGroup
                .SelectMany(b => b.Passengers)
                .Select(p => p.TripSeat?.Seat?.Code)
                .Where(code => !string.IsNullOrWhiteSpace(code))
                .Distinct()
                .Select(code => new TripSeatStatusChange(code!, "Available"))
                .ToList();
            if (releasedSeats.Count > 0)
            {
                await notifier.PublishSeatStatusChangedAsync(tripGroup.Key, releasedSeats, cancellationToken);
            }
        }

        return overdueBookings.Count;
    }

    private static async Task NotifySeatsReleasedAsync(
        IApplicationDbContext context,
        ITripSeatNotifier notifier,
        Booking booking,
        CancellationToken cancellationToken)
    {
        if (!booking.TripId.HasValue)
        {
            return;
        }

        var seatCodes = await context.Set<BookingPassenger>()
            .Where(p => p.BookingId == booking.Id && p.TripSeatId.HasValue)
            .Select(p => p.TripSeat!.Seat.Code)
            .ToListAsync(cancellationToken);
        if (seatCodes.Count == 0)
        {
            return;
        }

        await notifier.PublishSeatStatusChangedAsync(
            booking.TripId.Value,
            seatCodes.Distinct().Select(code => new TripSeatStatusChange(code, "Available")).ToList(),
            cancellationToken);
    }
}
