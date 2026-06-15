using NetTopologySuite.Geometries;

namespace SaigonWaterbus.Domain.Entities;

public class RouteSegment : BaseGuidAuditableEntity
{
    public Guid RouteId { get; set; }

    public Guid FromStationId { get; set; }

    public Guid ToStationId { get; set; }

    public int SegmentOrder { get; set; }

    public decimal DistanceKm { get; set; }

    public int EstimatedTravelMinutes { get; set; }

    public LineString? Geometry { get; set; }

    public Route Route { get; set; } = null!;

    public Station FromStation { get; set; } = null!;

    public Station ToStation { get; set; } = null!;
}
