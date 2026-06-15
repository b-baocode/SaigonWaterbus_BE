using NetTopologySuite.Geometries;
using SaigonWaterbus.Domain.Common;

namespace SaigonWaterbus.Domain.Entities;

public class Route : BaseGuidAuditableEntity
{
    public string RouteCode { get; set; } = null!;
    public string RouteName { get; set; } = null!;
    public string? Description { get; set; }
    public decimal? BaseDistanceKm { get; set; }
    public int? EstimatedDurationMin { get; set; }
    public string Status { get; set; } = "Active";
    public LineString? RouteGeometry { get; set; }
    public string? OsmId { get; set; }

    public ICollection<RouteStop> RouteStops { get; set; } = new List<RouteStop>();
    public ICollection<RouteSegment> RouteSegments { get; set; } = new List<RouteSegment>();
    public ICollection<Trip> Trips { get; set; } = new List<Trip>();
    public ICollection<FareMatrix> FareMatrices { get; set; } = new List<FareMatrix>();
}
