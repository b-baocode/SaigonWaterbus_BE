using NetTopologySuite.Geometries;
using SaigonWaterbus.Domain.Common;

namespace SaigonWaterbus.Domain.Entities;

public class Station : BaseGuidAuditableEntity
{
    public string StationCode { get; set; } = null!;
    public string StationName { get; set; } = null!;
    public string? Address { get; set; }
    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }
    public string Status { get; set; } = "Active";
    public Point? Location { get; set; }
    public string? OsmId { get; set; }

    public ICollection<RouteStop> RouteStops { get; set; } = new List<RouteStop>();
    public ICollection<Landmark> Landmarks { get; set; } = new List<Landmark>();
}
