namespace SaigonWaterbus.Application.Common.Interfaces;

public interface IIncidentGpsHookNotifier
{
    Task NotifyAsync(
        IncidentGpsHookNotification notification,
        CancellationToken cancellationToken);
}

public sealed record IncidentGpsHookNotification(
    string Event,
    Guid IncidentId,
    Guid? TripId,
    string? TripCode,
    Guid BoatId,
    string BoatCode,
    Guid? RescueBoatId,
    string? RescueBoatCode,
    Guid? ReplacementBoatId,
    string? ReplacementBoatCode,
    string ReplacementMissionType,
    Guid? ReplacementTargetStationId,
    string? ReplacementTargetStationCode,
    string? ReplacementTargetStationName,
    int? ReplacementTargetStopOrder,
    decimal? ReplacementTargetLat,
    decimal? ReplacementTargetLng,
    int ReplacementDelayMinutes,
    DateTimeOffset? ReplacementEstimatedResumeAt,
    int OnboardPassengerCount,
    int FuturePassengerCount,
    decimal? Lat,
    decimal? Lng);

public sealed class NullIncidentGpsHookNotifier : IIncidentGpsHookNotifier
{
    public static readonly NullIncidentGpsHookNotifier Instance = new();

    private NullIncidentGpsHookNotifier() { }

    public Task NotifyAsync(
        IncidentGpsHookNotification notification,
        CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
