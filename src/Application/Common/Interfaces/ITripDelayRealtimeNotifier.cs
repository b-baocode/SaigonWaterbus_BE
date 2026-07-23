namespace SaigonWaterbus.Application.Common.Interfaces;

public interface ITripDelayRealtimeNotifier
{
    Task PublishUpdatedAsync(
        TripDelayRealtimeEvent delayEvent,
        CancellationToken cancellationToken);
}

public sealed record TripDelayRealtimeEvent(
    Guid TripId,
    string TripCode,
    Guid? BoatId,
    string? BoatCode,
    string TripStatus,
    DateTimeOffset? AdjustedDepartureTime,
    DateTimeOffset? AdjustedArrivalTime,
    int DelayMinutes,
    string? DelayReason,
    bool IsDelayActive,
    DateTimeOffset? DelayStartedAt,
    DateTimeOffset? DelayEndedAt,
    int? DelayStartStopOrder,
    int DelayPropagationMinutes,
    IReadOnlyList<TripDelayAffectedTripRealtimeEvent> AffectedTrips);

public sealed record TripDelayAffectedTripRealtimeEvent(
    Guid TripId,
    string TripCode,
    int AddedDelayMinutes,
    int TotalDelayMinutes,
    DateTimeOffset? AdjustedDepartureTime,
    DateTimeOffset? AdjustedArrivalTime);

public sealed class NullTripDelayRealtimeNotifier : ITripDelayRealtimeNotifier
{
    public static readonly NullTripDelayRealtimeNotifier Instance = new();

    private NullTripDelayRealtimeNotifier() { }

    public Task PublishUpdatedAsync(
        TripDelayRealtimeEvent delayEvent,
        CancellationToken cancellationToken) => Task.CompletedTask;
}
