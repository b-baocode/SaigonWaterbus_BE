using SaigonWaterbus.Domain.Common;

namespace SaigonWaterbus.Domain.Entities;

public sealed class IncidentMissionEvent : BaseGuidEntity
{
    public Guid IncidentId { get; set; }
    public string GpsEventId { get; set; } = null!;
    public string Event { get; set; } = null!;
    public string BoatCode { get; set; } = null!;
    public DateTimeOffset OccurredAt { get; set; }
    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }
    public Guid? StationId { get; set; }
    public string? StationCode { get; set; }
    public string? Note { get; set; }
    public string? ReportedPreviousMissionStatus { get; set; }
    public int? EstimatedTowingMinutes { get; set; }
    public string PreviousMissionStatus { get; set; } = null!;
    public string MissionStatus { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }

    public Incident Incident { get; set; } = null!;
    public Station? Station { get; set; }
}
