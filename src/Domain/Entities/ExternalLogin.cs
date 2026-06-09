namespace SaigonWaterbus.Domain.Entities;

public class ExternalLogin : BaseEntity
{
    public Guid UserId { get; set; }

    public string Provider { get; set; } = null!; // "google", "facebook"

    public string ProviderUserId { get; set; } = null!; // ID từ provider

    public string? Email { get; set; }

    public string? DisplayName { get; set; }

    public string? ProfilePictureUrl { get; set; }

    public DateTimeOffset LinkedAt { get; set; }

    public User? User { get; set; }
}
