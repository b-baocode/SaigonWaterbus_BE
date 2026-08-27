using Microsoft.AspNetCore.SignalR;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Web.Hubs;

namespace SaigonWaterbus.Web.Infrastructure;

public sealed class SignalRTripsResetRealtimeNotifier : ITripsResetRealtimeNotifier
{
    private readonly IHubContext<TrackingHub> _hubContext;
    private readonly ILogger<SignalRTripsResetRealtimeNotifier> _logger;

    public SignalRTripsResetRealtimeNotifier(
        IHubContext<TrackingHub> hubContext,
        ILogger<SignalRTripsResetRealtimeNotifier> logger)
    {
        _hubContext = hubContext;
        _logger = logger;
    }

    public async Task PublishResetAsync(
        TripsResetRealtimeEvent resetEvent,
        CancellationToken cancellationToken)
    {
        try
        {
            // Broadcast theo boat group: thiết bị GPS / dashboard của đúng tàu đó mới nhận.
            // Kèm broadcast global (Clients.All) để các UI quản trị theo dõi nhiều tàu cũng đồng bộ.
            var boatGroup = _hubContext.Clients
                .Group(TrackingHub.BoatGroupName(resetEvent.BoatId));
            await boatGroup.SendAsync(
                TrackingHub.TripsResetEventName,
                resetEvent,
                cancellationToken);

            await _hubContext.Clients.All.SendAsync(
                TrackingHub.TripsResetEventName,
                resetEvent,
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(
                ex,
                "Failed to broadcast trips reset event for boat {BoatCode} on {OperatingDate:yyyy-MM-dd}.",
                resetEvent.BoatCode,
                resetEvent.OperatingDate);
        }
    }
}