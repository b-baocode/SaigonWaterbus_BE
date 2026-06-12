using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Domain.Entities;

public class VesselRentalPrice : BaseGuidAuditableEntity
{
    public Guid VesselId { get; set; }

    public Vessel Vessel { get; set; } = null!;

    public VesselRentalUnit RentalUnit { get; set; } = VesselRentalUnit.Day;

    public decimal UnitPrice { get; set; }

    public string Currency { get; set; } = "VND";

    public string? Note { get; set; }
}
