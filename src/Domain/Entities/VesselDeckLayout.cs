namespace SaigonWaterbus.Domain.Entities;

public class VesselDeckLayout : BaseAuditableEntity
{
    public Guid VesselId { get; set; }

    public Vessel Vessel { get; set; } = null!;

    public int DeckNumber { get; set; }

    public int RowCount { get; set; }

    public int ColumnCount { get; set; }
}
