namespace SaigonWaterbus.Domain.Entities;

public class VesselImage : BaseAuditableEntity
{
    public Guid VesselId { get; set; }

    public string Url { get; set; } = null!;

    public string? PublicId { get; set; }

    public int DisplayOrder { get; set; }

    public bool IsPrimary { get; set; }

    public Vessel Vessel { get; set; } = null!;
}
