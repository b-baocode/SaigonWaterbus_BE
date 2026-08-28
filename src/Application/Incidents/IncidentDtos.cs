using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Application.Incidents;

public sealed record IncidentDto(
    Guid IncidentId,
    Guid BoatId,
    string BoatName,
    string BoatCode,
    Guid? TripId,
    string? TripCode,
    string IncidentType,
    string Description,
    string? Severity,
    DateTimeOffset OccurredAt,
    string ResolutionStatus,
    Guid? ReportedByUserId,
    string? ReportedByName,
    DateTimeOffset? AssignedAt,
    Guid? AssignedByUserId,
    string? AssignedByName,
    Guid? RescueBoatId,
    string? RescueBoatName,
    string? RescueBoatCode,
    DateTimeOffset? RescueDispatchedAt,
    Guid? RescueDispatchedByUserId,
    string? RescueDispatchedByName,
    Guid? ReplacementBoatId,
    string? ReplacementBoatName,
    DateTimeOffset? ReplacementAssignedAt,
    Guid? ReplacementAssignedByUserId,
    string? ReplacementAssignedByName,
    string? ReplacementMissionType,
    Guid? ReplacementTargetStationId,
    string? ReplacementTargetStationName,
    int? ReplacementTargetStopOrder,
    int? ReplacementDelayMinutes,
    DateTimeOffset? ReplacementEstimatedResumeAt,
    int ActiveTicketCount,
    int OnboardPassengerCount,
    int FuturePassengerCount,
    string? ResolutionNote,
    DateTimeOffset? ResolvedAt);

public sealed record IncidentPassengerImpactPlan(
    int ActiveTicketCount,
    int OnboardPassengerCount,
    int FuturePassengerCount,
    string MissionType,
    Guid? TargetStationId,
    string? TargetStationCode,
    string? TargetStationName,
    int? TargetStopOrder,
    DateTimeOffset? TargetPlannedArrivalAt,
    DateTimeOffset? TargetPlannedDepartureAt)
{
    public int AffectedPassengerCount => OnboardPassengerCount + FuturePassengerCount;

    public string ReplacementMissionType { get; init; } = MissionType;

    public static IncidentPassengerImpactPlan Empty { get; } = new(
        ActiveTicketCount: 0,
        OnboardPassengerCount: 0,
        FuturePassengerCount: 0,
        MissionType: "None",
        TargetStationId: null,
        TargetStationCode: null,
        TargetStationName: null,
        TargetStopOrder: null,
        TargetPlannedArrivalAt: null,
        TargetPlannedDepartureAt: null);
}

public sealed record IncidentStopPlanItem(
    Guid StationId,
    string StationCode,
    string StationName,
    int StopOrder,
    DateTimeOffset? PlannedArrivalTime,
    DateTimeOffset? PlannedDepartureTime,
    string? StopStatus,
    DateTimeOffset? ActualArrivalTime,
    DateTimeOffset? ActualDepartureTime);

public sealed record TicketTripSegment(
    int? FromStopOrder,
    int? ToStopOrder,
    TicketStatus TicketStatus,
    DateTimeOffset? CheckedOutAt)
{
    public bool IsOnboard => TicketStatus == TicketStatus.CheckedIn
        || TicketStatus == TicketStatus.CheckedOut;

    public bool CanBoardLater => TicketStatus == TicketStatus.Active;
}
