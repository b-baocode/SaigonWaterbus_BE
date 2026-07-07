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

/// <summary>Status: Available | Held | Booked.</summary>
public sealed record TripSeatStatusChange(string SeatCode, string Status);

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
