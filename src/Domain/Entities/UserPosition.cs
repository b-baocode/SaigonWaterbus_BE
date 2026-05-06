namespace SaigonWaterbus.Domain.Entities;

public class UserPosition : BaseAuditableEntity
{
    public int UserId { get; set; }

    public int PositionId { get; set; }

    public int? StationId { get; set; }

    public int? AssignedByUserId { get; set; }

    public DateTimeOffset AssignedAt { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }

    public bool IsActive { get; set; }

    public User User { get; set; } = null!;

    public StaffPosition Position { get; set; } = null!;

    public User? AssignedByUser { get; set; }
}
