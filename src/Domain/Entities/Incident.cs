using SaigonWaterbus.Domain.Common;

namespace SaigonWaterbus.Domain.Entities;

public class Incident : BaseGuidEntity
{
    public Guid TripId { get; set; }
    public int? ReportedBy { get; set; }
    public string IncidentType { get; set; } = null!;
    public string Description { get; set; } = null!;
    public string? Severity { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public string ResolutionStatus { get; set; } = "Open";

    public Trip Trip { get; set; } = null!;
    public User? Reporter { get; set; }
}
