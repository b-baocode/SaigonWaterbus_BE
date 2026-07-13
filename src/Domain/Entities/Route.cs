using NetTopologySuite.Geometries;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Common;

namespace SaigonWaterbus.Domain.Entities;

public class Route : BaseGuidAuditableEntity
{
    public string RouteCode { get; set; } = null!;
    public string RouteName { get; set; } = null!;
    public string RouteType { get; set; } = RouteTypes.Regular;
    public string? Description { get; set; }
    public decimal? BaseDistanceKm { get; set; }
    public int? EstimatedDurationMin { get; set; }
    public string Status { get; set; } = "Active";
    public bool IsBookable { get; set; } = true;
    public LineString? RouteGeometry { get; set; }
    public string? OsmId { get; set; }

    public ICollection<RouteStop> RouteStops { get; set; } = new List<RouteStop>();
    public ICollection<Trip> Trips { get; set; } = new List<Trip>();
}
