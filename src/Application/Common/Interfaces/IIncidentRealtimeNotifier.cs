namespace SaigonWaterbus.Application.Common.Interfaces;

/// <summary>
/// Phát realtime event cho màn điều hành sự cố/cứu hộ.
/// Client nhận event rồi có thể refetch REST API chi tiết theo quyền hiện tại.
/// </summary>
public interface IIncidentRealtimeNotifier
{
    Task PublishChangedAsync(
        IncidentRealtimeEvent change,
        CancellationToken cancellationToken);
}

public sealed record IncidentRealtimeEvent(
    Guid IncidentId,
    string EventType,
    Guid BoatId,
    string? BoatName = null,
    Guid? TripId = null,
    string? TripCode = null,
    Guid? RescueBoatId = null,
    string? RescueBoatName = null,
    Guid? ReplacementBoatId = null,
    string? ReplacementBoatName = null,
    string? ReplacementMissionType = null,
    Guid? ReplacementTargetStationId = null,
    string? ReplacementTargetStationName = null,
    int? ReplacementDelayMinutes = null,
    DateTimeOffset? ReplacementEstimatedResumeAt = null,
    int? OnboardPassengerCount = null,
    int? FuturePassengerCount = null,
    string? ResolutionStatus = null,
    DateTimeOffset? OccurredAt = null);

public sealed class NullIncidentRealtimeNotifier : IIncidentRealtimeNotifier
{
    public static readonly NullIncidentRealtimeNotifier Instance = new();

    private NullIncidentRealtimeNotifier() { }

    public Task PublishChangedAsync(
        IncidentRealtimeEvent change,
        CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
