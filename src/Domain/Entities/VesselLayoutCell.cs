using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Domain.Entities;

public class VesselLayoutCell : BaseAuditableEntity
{
    public Guid VesselId { get; set; }

    public Vessel Vessel { get; set; } = null!;

    public VesselLayoutCellType Type { get; set; }

    public int Deck { get; set; }

    public string Row { get; set; } = null!;

    public int Column { get; set; }
}
