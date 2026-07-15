namespace SaigonWaterbus.Application.Common.Interfaces;

/// <summary>
/// Phát sự kiện thay đổi trạng thái ghế của một chuyến tới các client đang xem sơ đồ ghế
/// (hiện thực bằng SignalR ở tầng Web).
/// </summary>
public interface ITripSeatNotifier
{
    Task PublishSeatStatusChangedAsync(
        Guid tripId,
        IReadOnlyList<TripSeatStatusChange> changes,
        CancellationToken cancellationToken);
}

/// <summary>
/// Status: Available | Held | Booked. FromStopOrder/ToStopOrder là chặng bị ảnh hưởng
/// (null = cả trip) — client đang xem chặng không giao nhau có thể bỏ qua sự kiện.
/// </summary>
public sealed record TripSeatStatusChange(
    string SeatCode,
    string Status,
    int? FromStopOrder = null,
    int? ToStopOrder = null);

public sealed class NullTripSeatNotifier : ITripSeatNotifier
{
    public static readonly NullTripSeatNotifier Instance = new();

    private NullTripSeatNotifier() { }

    public Task PublishSeatStatusChangedAsync(
        Guid tripId,
        IReadOnlyList<TripSeatStatusChange> changes,
        CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
