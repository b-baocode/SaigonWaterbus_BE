using SaigonWaterbus.Domain.Common;

namespace SaigonWaterbus.Domain.Entities;

public class RouteStop : BaseGuidAuditableEntity
{
    public Guid RouteId { get; set; }
    public Guid StationId { get; set; }
    public int StopOrder { get; set; }
    public int? StandardTravelMin { get; set; }
    public int? StandardDwellMin { get; set; }
    public bool IsPickupAllowed { get; set; } = true;
    public bool IsDropoffAllowed { get; set; } = true;

    public Route Route { get; set; } = null!;
    public Station Station { get; set; } = null!;
    public ICollection<TripStop> TripStops { get; set; } = new List<TripStop>();
}
