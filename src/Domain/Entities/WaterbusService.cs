namespace SaigonWaterbus.Domain.Entities;

public class WaterbusService : BaseAuditableEntity
{
    public string Code { get; set; } = null!;

    public string Name { get; set; } = null!;

    public string? Description { get; set; }

    public bool IsActive { get; set; } = true;

    public int DisplayOrder { get; set; }

    public ICollection<Vessel> Vessels { get; set; } = new List<Vessel>();
}
