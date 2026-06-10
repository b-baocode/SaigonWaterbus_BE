using SaigonWaterbus.Domain.Common;

namespace SaigonWaterbus.Domain.Entities;

public class Landmark : BaseGuidAuditableEntity
{
    public Guid StationId { get; set; }
    public string LandmarkName { get; set; } = null!;
    public string? Description { get; set; }
    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }
    public string? AudioViUrl { get; set; }
    public string? AudioEnUrl { get; set; }
    public bool IsActive { get; set; } = true;

    public Station Station { get; set; } = null!;
}
