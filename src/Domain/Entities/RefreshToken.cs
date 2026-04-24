namespace SaigonWaterbus.Domain.Entities;

public class RefreshToken : BaseAuditableEntity
{
    public int UserId { get; set; }

    public string TokenHash { get; set; } = null!;

    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }

    public string? DeviceName { get; set; }

    public string? IpAddress { get; set; }

    public string? UserAgent { get; set; }

    public User User { get; set; } = null!;
}
