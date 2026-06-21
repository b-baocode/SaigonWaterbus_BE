namespace SaigonWaterbus.Domain.Entities;

public class StationImage : BaseGuidAuditableEntity
{
    public Guid StationId { get; set; }

    public string Url { get; set; } = null!;

    public string? PublicId { get; set; }

    public int DisplayOrder { get; set; }

    public bool IsPrimary { get; set; }

    public Station Station { get; set; } = null!;
}
