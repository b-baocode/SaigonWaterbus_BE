namespace SaigonWaterbus.Domain.Entities;

public class UserStationAssignment : BaseAuditableEntity
{
    public Guid UserId { get; set; }

    public User User { get; set; } = null!;

    public Guid StationId { get; set; }

    public Station Station { get; set; } = null!;

    public bool IsPrimary { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTimeOffset AssignedAt { get; set; }

    public Guid AssignedByUserId { get; set; }

    public User AssignedByUser { get; set; } = null!;
}
