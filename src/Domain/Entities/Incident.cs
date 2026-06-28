using SaigonWaterbus.Domain.Common;

namespace SaigonWaterbus.Domain.Entities;

public class Incident : BaseGuidEntity
{
    public Guid BoatId { get; set; }
    public Guid? TripId { get; set; }
    public Guid? ReportedBy { get; set; }
    public string IncidentType { get; set; } = null!;
    public string Description { get; set; } = null!;
    public string? Severity { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public string ResolutionStatus { get; set; } = "Open";
    public Guid? AssignedManagerId { get; set; }
    public DateTimeOffset? AssignedAt { get; set; }
    public Guid? AssignedByUserId { get; set; }
    public Guid? ReplacementBoatId { get; set; }
    public DateTimeOffset? ReplacementAssignedAt { get; set; }
    public Guid? ReplacementAssignedByUserId { get; set; }
    public string? ReplacementNote { get; set; }
    public string? ResolutionNote { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
    public Guid? ResolvedByUserId { get; set; }

    public Boat Boat { get; set; } = null!;
    public Trip? Trip { get; set; }
    public User? Reporter { get; set; }
    public User? AssignedManager { get; set; }
    public User? AssignedByUser { get; set; }
    public Boat? ReplacementBoat { get; set; }
    public User? ReplacementAssignedByUser { get; set; }
    public User? Resolver { get; set; }
}
