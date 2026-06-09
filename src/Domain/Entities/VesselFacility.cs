using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Domain.Entities;

public class VesselFacility : BaseAuditableEntity
{
    public Guid VesselId { get; set; }

    public Vessel Vessel { get; set; } = null!;

    public VesselFacilityType Type { get; set; }

    public int Deck { get; set; }

    public string Row { get; set; } = null!;

    public int Column { get; set; }

    public int RowSpan { get; set; }

    public int ColumnSpan { get; set; }

    public bool IsActive { get; set; } = true;
}
