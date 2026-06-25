using SaigonWaterbus.Domain.Common;

namespace SaigonWaterbus.Domain.Entities;

public class FareMatrix : BaseGuidAuditableEntity
{
    public Guid? ServiceId { get; set; }
    public Guid RouteId { get; set; }
    public Guid FromStationId { get; set; }
    public Guid ToStationId { get; set; }
    public string? ServicePeriod { get; set; }
    public decimal BasePrice { get; set; }
    public string Currency { get; set; } = "VND";
    public bool IsActive { get; set; } = true;

    public WaterbusService? Service { get; set; }
    public Route Route { get; set; } = null!;
    public Station FromStation { get; set; } = null!;
    public Station ToStation { get; set; } = null!;
}
