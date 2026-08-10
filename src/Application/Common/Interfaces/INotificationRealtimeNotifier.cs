namespace SaigonWaterbus.Application.Common.Interfaces;

/// <summary>
/// Đẩy in-app notification vừa tạo tới đúng user qua kênh realtime (SignalR).
/// Implementation không được ném lỗi ra ngoài — push realtime thất bại thì thông báo
/// vẫn còn trong DB, client đọc lại qua API.
/// </summary>
public interface INotificationRealtimeNotifier
{
    Task PublishCreatedAsync(
        IReadOnlyList<NotificationRealtimeEvent> notifications,
        CancellationToken cancellationToken);
}

public sealed record NotificationRealtimeEvent(
    Guid NotificationId,
    Guid UserId,
    string Title,
    string? Body,
    string Type,
    string? RelatedEntityType,
    Guid? RelatedEntityId,
    DateTimeOffset CreatedAt);

public sealed class NullNotificationRealtimeNotifier : INotificationRealtimeNotifier
{
    public static readonly NullNotificationRealtimeNotifier Instance = new();

    private NullNotificationRealtimeNotifier() { }

    public Task PublishCreatedAsync(
        IReadOnlyList<NotificationRealtimeEvent> notifications,
        CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>
/// Null-object: gửi push lên Expo, dùng khi push sender chưa được DI inject
/// (test, dev, hoặc trước khi feature flag bật).
/// </summary>
public sealed class NullPushNotificationSender : IPushNotificationSender
{
    public static readonly NullPushNotificationSender Instance = new();

    private NullPushNotificationSender() { }

    public Task SendAsync(
        Guid userId,
        PushNotificationPayload payload,
        CancellationToken cancellationToken) => Task.CompletedTask;
}
