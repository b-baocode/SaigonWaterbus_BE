namespace SaigonWaterbus.Application.Common.Interfaces;

/// <summary>
/// Gửi push notification tới mobile device thông qua Expo Push API.
/// Implementation: <c>ExpoPushNotificationSender</c>.
/// </summary>
public interface IPushNotificationSender
{
    /// <summary>
    /// Gửi 1 push notification tới tất cả thiết bị active của user.
    /// Token invalid sẽ tự động bị disable.
    /// </summary>
    Task SendAsync(
        Guid userId,
        PushNotificationPayload payload,
        CancellationToken cancellationToken);
}

public sealed record PushNotificationPayload(
    string Title,
    string Body,
    string Type,
    string? RelatedEntityType = null,
    Guid? RelatedEntityId = null,
    string? DeepLink = null,
    string ChannelId = "default",
    string Priority = "high");
