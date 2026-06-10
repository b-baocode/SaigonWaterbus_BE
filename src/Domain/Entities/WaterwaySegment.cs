using NetTopologySuite.Geometries;
using SaigonWaterbus.Domain.Common;

namespace SaigonWaterbus.Domain.Entities;

public class WaterwaySegment : BaseGuidAuditableEntity
{
    public string? OsmId { get; set; }
    public string? WaterwayName { get; set; }
    public string WaterwayType { get; set; } = null!;
    public int SegmentOrder { get; set; }
    public decimal LengthKm { get; set; }
    public LineString Geometry { get; set; } = null!;
}
