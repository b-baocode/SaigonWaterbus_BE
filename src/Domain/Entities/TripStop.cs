using SaigonWaterbus.Domain.Common;
using SaigonWaterbus.Domain.Constants;

namespace SaigonWaterbus.Domain.Entities;

/// <summary>
/// Lich trinh tung ben cua mot trip (hien dung cho trip charter):
/// thu tu ben, thoi gian dung va gio den/di du kien.
/// </summary>
public class TripStop : BaseGuidAuditableEntity
{
    public Guid TripId { get; set; }
    public Guid StationId { get; set; }
    public int StopOrder { get; set; }
    public int StayDurationMinutes { get; set; }
    public DateTimeOffset? PlannedArrivalTime { get; set; }
    public DateTimeOffset? PlannedDepartureTime { get; set; }
    public DateTimeOffset? AdjustedArrivalTime { get; set; }
    public DateTimeOffset? AdjustedDepartureTime { get; set; }
    public DateTimeOffset? ActualArrivalTime { get; set; }
    public DateTimeOffset? ActualDepartureTime { get; set; }
    public string StopStatus { get; set; } = TripStopStatuses.Scheduled;
    public string? Note { get; set; }

    public Trip Trip { get; set; } = null!;
    public Station Station { get; set; } = null!;
}
