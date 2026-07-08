using Microsoft.AspNetCore.SignalR;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Web.Hubs;

namespace SaigonWaterbus.Web.Infrastructure;

public sealed class SignalRCharterBookingRealtimeNotifier : ICharterBookingRealtimeNotifier
{
    private readonly IHubContext<CharterBookingsHub> _hubContext;
    private readonly ILogger<SignalRCharterBookingRealtimeNotifier> _logger;
    private readonly TimeProvider _timeProvider;

    public SignalRCharterBookingRealtimeNotifier(
        IHubContext<CharterBookingsHub> hubContext,
        ILogger<SignalRCharterBookingRealtimeNotifier> logger,
        TimeProvider timeProvider)
    {
        _hubContext = hubContext;
        _logger = logger;
        _timeProvider = timeProvider;
    }

    public async Task PublishChangedAsync(
        CharterBookingRealtimeEvent change,
        CancellationToken cancellationToken)
    {
        var payload = new CharterBookingRealtimeEvent(
            change.BookingId,
            change.EventType,
            change.BookingStatus,
            change.PaymentStatus,
            change.OccurredAt ?? _timeProvider.GetUtcNow());

        try
        {
            await _hubContext.Clients
                .Group(CharterBookingsHub.BookingGroupName(change.BookingId))
                .SendAsync(CharterBookingsHub.ChangedEventName, payload, cancellationToken);

            await _hubContext.Clients
                .Group(CharterBookingsHub.AssignedListGroupName)
                .SendAsync(CharterBookingsHub.AssignedListChangedEventName, payload, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex,
                "Failed to broadcast charter booking realtime event {EventType} for booking {BookingId}.",
                change.EventType,
                change.BookingId);
        }
    }
}
