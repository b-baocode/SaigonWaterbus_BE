using Microsoft.AspNetCore.SignalR;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Web.Hubs;

namespace SaigonWaterbus.Web.Infrastructure;

public sealed class SignalRTripDelayRealtimeNotifier : ITripDelayRealtimeNotifier
{
    private readonly IHubContext<TrackingHub> _hubContext;
    private readonly ILogger<SignalRTripDelayRealtimeNotifier> _logger;

    public SignalRTripDelayRealtimeNotifier(
        IHubContext<TrackingHub> hubContext,
        ILogger<SignalRTripDelayRealtimeNotifier> logger)
    {
        _hubContext = hubContext;
        _logger = logger;
    }

    public async Task PublishUpdatedAsync(
        TripDelayRealtimeEvent change,
        CancellationToken cancellationToken)
    {
        if (!change.BoatId.HasValue)
        {
            return;
        }

        try
        {
            await _hubContext.Clients
                .Group(TrackingHub.BoatGroupName(change.BoatId.Value))
                .SendAsync(
                    TrackingHub.TripDelayUpdatedEventName,
                    change,
                    cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(
                ex,
                "Failed to broadcast trip delay event for trip {TripCode}.",
                change.TripCode);
        }
    }
}
