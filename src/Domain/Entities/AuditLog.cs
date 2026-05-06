namespace SaigonWaterbus.Domain.Entities;

public class AuditLog : BaseEntity
{
    public int? ActorUserId { get; set; }

    public string Action { get; set; } = null!;

    public string TargetTable { get; set; } = null!;

    public int? TargetId { get; set; }

    public string? OldValues { get; set; }

    public string? NewValues { get; set; }

    public string? IpAddress { get; set; }

    public string? UserAgent { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public User? ActorUser { get; set; }
}
