namespace SaigonWaterbus.Domain.Entities;

public class GpsDevice : BaseGuidAuditableEntity
{
    public string DeviceId { get; set; } = null!;

    public Guid BoatId { get; set; }

    public bool IsActive { get; set; } = true;

    public long? LastSequence { get; set; }

    public DateTimeOffset? LastSeenAt { get; set; }

    public Boat Boat { get; set; } = null!;
}
