using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Application.Bookings;

/// <summary>
/// Phát sự kiện nhả ghế realtime khi booking bị hủy / hết hạn giữ chỗ. Ghế được nhả THEO ĐÚNG
/// CHẶNG của từng vé — một ghế trên trip Regular có thể đang có vé khác trên chặng không giao nhau,
/// nhả "cả trip" sẽ làm sơ đồ ghế của client hiển thị trống sai. Vé sightseeing / dữ liệu cũ
/// không có stop order thì giữ null = nhả cả trip như trước.
/// </summary>
public static class SeatReleaseNotificationSupport
{
    private const string AvailableStatus = "Available";

    private sealed record ReleasedSeat(Guid TripId, string SeatCode, int? FromStopOrder, int? ToStopOrder);

    /// <summary>Nhả ghế của một booking (query passengers từ DB).</summary>
    public static async Task NotifyBookingSeatsReleasedAsync(
        IApplicationDbContext context,
        ITripSeatNotifier notifier,
        Guid bookingId,
        CancellationToken cancellationToken)
    {
        var seats = await context.Set<BookingPassenger>()
            .Where(p => p.BookingId == bookingId && p.TripSeatId.HasValue)
            .Select(p => new ReleasedSeat(
                p.TripSeat!.TripId, p.TripSeat.Seat.Code, p.FromStopOrder, p.ToStopOrder))
            .ToListAsync(cancellationToken);

        await PublishAsync(notifier, seats, cancellationToken);
    }

    /// <summary>Nhả ghế của nhiều booking đã load sẵn passengers (job quét hết hạn).</summary>
    public static Task NotifyPassengerSeatsReleasedAsync(
        ITripSeatNotifier notifier,
        IEnumerable<BookingPassenger> passengers,
        CancellationToken cancellationToken) =>
        PublishAsync(
            notifier,
            passengers
                .Where(p => p.TripSeat?.Seat != null && !string.IsNullOrWhiteSpace(p.TripSeat.Seat.Code))
                .Select(p => new ReleasedSeat(
                    p.TripSeat!.TripId, p.TripSeat.Seat!.Code, p.FromStopOrder, p.ToStopOrder))
                .ToList(),
            cancellationToken);

    private static async Task PublishAsync(
        ITripSeatNotifier notifier,
        IReadOnlyList<ReleasedSeat> seats,
        CancellationToken cancellationToken)
    {
        // Nhóm theo trip của từng ghế — booking khứ hồi giữ ghế trên 2 trip.
        foreach (var tripGroup in seats.GroupBy(s => s.TripId))
        {
            var changes = tripGroup
                .Select(s => new TripSeatStatusChange(
                    s.SeatCode, AvailableStatus, s.FromStopOrder, s.ToStopOrder))
                .Distinct()
                .ToList();
            await notifier.PublishSeatStatusChangedAsync(tripGroup.Key, changes, cancellationToken);
        }
    }
}
