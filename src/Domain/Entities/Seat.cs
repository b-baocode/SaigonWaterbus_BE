namespace SaigonWaterbus.Domain.Entities;

public class Seat : BaseAuditableEntity
{
    public Guid VesselId { get; set; }

    public Vessel Vessel { get; set; } = null!;

    public string Code { get; set; } = null!;

    public int Deck { get; set; }

    public string Row { get; set; } = null!;

    public int Column { get; set; }

    public bool IsActive { get; set; } = true;
}
