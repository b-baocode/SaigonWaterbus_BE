using SaigonWaterbus.Domain.Common;

namespace SaigonWaterbus.Domain.Entities;

public class TripStop : BaseGuidAuditableEntity
{
    public Guid TripId { get; set; }
    public Guid RouteStopId { get; set; }
    public int StopOrder { get; set; }
    public DateTimeOffset? ScheduledArrival { get; set; }
    public DateTimeOffset? ScheduledDeparture { get; set; }
    public DateTimeOffset? ActualArrival { get; set; }
    public DateTimeOffset? ActualDeparture { get; set; }
    public string StopStatus { get; set; } = "Scheduled";

    public Trip Trip { get; set; } = null!;
    public RouteStop RouteStop { get; set; } = null!;
}
