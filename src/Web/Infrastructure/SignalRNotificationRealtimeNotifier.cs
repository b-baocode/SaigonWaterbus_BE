using Microsoft.AspNetCore.SignalR;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Web.Hubs;

namespace SaigonWaterbus.Web.Infrastructure;

public sealed class SignalRNotificationRealtimeNotifier : INotificationRealtimeNotifier
{
    private readonly IHubContext<NotificationsHub> _hubContext;
    private readonly ILogger<SignalRNotificationRealtimeNotifier> _logger;

    public SignalRNotificationRealtimeNotifier(
        IHubContext<NotificationsHub> hubContext,
        ILogger<SignalRNotificationRealtimeNotifier> logger)
    {
        _hubContext = hubContext;
        _logger = logger;
    }

    public async Task PublishCreatedAsync(
        IReadOnlyList<NotificationRealtimeEvent> notifications,
        CancellationToken cancellationToken)
    {
        try
        {
            foreach (var notification in notifications)
            {
                await _hubContext.Clients
                    .User(notification.UserId.ToString())
                    .SendAsync(NotificationsHub.CreatedEventName, notification, cancellationToken);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(
                ex,
                "Failed to push {NotificationCount} realtime in-app notifications.",
                notifications.Count);
        }
    }
}
