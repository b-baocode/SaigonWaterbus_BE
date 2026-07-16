using Microsoft.AspNetCore.SignalR;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Web.Hubs;

namespace SaigonWaterbus.Web.Infrastructure;

public sealed class SignalRIncidentRealtimeNotifier : IIncidentRealtimeNotifier
{
    private const string RescueDispatchedEventType = "RescueDispatched";

    private readonly IHubContext<IncidentsHub> _hubContext;
    private readonly ILogger<SignalRIncidentRealtimeNotifier> _logger;
    private readonly TimeProvider _timeProvider;

    public SignalRIncidentRealtimeNotifier(
        IHubContext<IncidentsHub> hubContext,
        ILogger<SignalRIncidentRealtimeNotifier> logger,
        TimeProvider timeProvider)
    {
        _hubContext = hubContext;
        _logger = logger;
        _timeProvider = timeProvider;
    }

    public async Task PublishChangedAsync(
        IncidentRealtimeEvent change,
        CancellationToken cancellationToken)
    {
        var payload = change with { OccurredAt = change.OccurredAt ?? _timeProvider.GetUtcNow() };

        try
        {
            await _hubContext.Clients.All.SendAsync(
                IncidentsHub.IncidentUpdatedEventName,
                payload,
                cancellationToken);

            if (string.Equals(change.EventType, RescueDispatchedEventType, StringComparison.Ordinal))
            {
                await _hubContext.Clients.All.SendAsync(
                    IncidentsHub.RescueDispatchedEventName,
                    payload,
                    cancellationToken);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(
                ex,
                "Failed to broadcast incident realtime event {EventType} for incident {IncidentId}.",
                change.EventType,
                change.IncidentId);
        }
    }
}
