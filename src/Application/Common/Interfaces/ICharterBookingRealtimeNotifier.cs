namespace SaigonWaterbus.Application.Common.Interfaces;

/// <summary>
/// Phát sự kiện thay đổi charter booking tới client realtime.
/// Payload cố ý nhẹ; client nhận event rồi refetch REST API theo quyền hiện tại.
/// </summary>
public interface ICharterBookingRealtimeNotifier
{
    Task PublishChangedAsync(
        CharterBookingRealtimeEvent change,
        CancellationToken cancellationToken);
}

public sealed record CharterBookingRealtimeEvent(
    Guid BookingId,
    string EventType,
    string? BookingStatus = null,
    string? PaymentStatus = null,
    DateTimeOffset? OccurredAt = null);

public sealed class NullCharterBookingRealtimeNotifier : ICharterBookingRealtimeNotifier
{
    public static readonly NullCharterBookingRealtimeNotifier Instance = new();

    private NullCharterBookingRealtimeNotifier() { }

    public Task PublishChangedAsync(
        CharterBookingRealtimeEvent change,
        CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
