namespace SaigonWaterbus.Domain.Entities;

public class RefreshToken : BaseAuditableEntity
{
    public int UserId { get; set; }

    public string TokenHash { get; set; } = null!;

    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }

    public User User { get; set; } = null!;
}
