using Microsoft.AspNetCore.SignalR;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Web.Hubs;

namespace SaigonWaterbus.Web.Infrastructure;

public sealed class SignalRTripSeatNotifier : ITripSeatNotifier
{
    private readonly IHubContext<TripSeatsHub> _hubContext;
    private readonly ILogger<SignalRTripSeatNotifier> _logger;

    public SignalRTripSeatNotifier(
        IHubContext<TripSeatsHub> hubContext,
        ILogger<SignalRTripSeatNotifier> logger)
    {
        _hubContext = hubContext;
        _logger = logger;
    }

    public async Task PublishSeatStatusChangedAsync(
        Guid tripId,
        IReadOnlyList<TripSeatStatusChange> changes,
        CancellationToken cancellationToken)
    {
        if (changes.Count == 0)
        {
            return;
        }

        try
        {
            await _hubContext.Clients
                .Group(TripSeatsHub.GroupName(tripId))
                .SendAsync(
                    "SeatStatusChanged",
                    new { tripId, seats = changes },
                    cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Sự kiện realtime chỉ là tối ưu UX; lỗi broadcast không được làm hỏng nghiệp vụ chính.
            _logger.LogWarning(ex, "Failed to broadcast seat status changes for trip {TripId}.", tripId);
        }
    }
}
