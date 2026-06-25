using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Domain.Entities;

public class OtpChallenge : BaseAuditableEntity
{
    public Guid UserId { get; set; }

    public OtpPurpose Purpose { get; set; }

    public OtpChannel Channel { get; set; } = OtpChannel.Email;

    public string Email { get; set; } = null!;

    public string? PendingPhoneNumber { get; set; }

    public string CodeHash { get; set; } = null!;

    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset ResendAvailableAt { get; set; }

    public DateTimeOffset? ConsumedAt { get; set; }

    public int AttemptCount { get; set; }

    public int MaxAttempts { get; set; }

    public User User { get; set; } = null!;
}
