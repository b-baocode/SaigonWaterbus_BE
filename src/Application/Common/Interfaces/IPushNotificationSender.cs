namespace SaigonWaterbus.Application.Common.Interfaces;

public interface IPushNotificationSender
{
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