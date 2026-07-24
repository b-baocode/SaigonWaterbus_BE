namespace SaigonWaterbus.Domain.Entities;

public class CharterRouteDrawRequestStop : BaseGuidAuditableEntity
{
    public Guid RequestId { get; set; }
    public Guid StationId { get; set; }
    public int StopOrder { get; set; }
    public string StationCode { get; set; } = null!;
    public string StationName { get; set; } = null!;
    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }
    public int StayDurationMinutes { get; set; }
    public string? Note { get; set; }

    public CharterRouteDrawRequest Request { get; set; } = null!;
    public Station Station { get; set; } = null!;
}
