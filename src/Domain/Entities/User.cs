using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Domain.Entities;

public class User : BaseAuditableEntity
{
    public string? UserCode { get; set; }

    public string FullName { get; set; } = null!;

    public DateOnly? DateOfBirth { get; set; }

    public string? PhoneNumber { get; set; }

    public string? NormalizedPhoneNumber { get; set; }

    public DateTimeOffset? PhoneVerifiedAt { get; set; }

    public string? Email { get; set; }

    public string? NormalizedEmail { get; set; }

    public string? PasswordHash { get; set; }

    public int RoleId { get; set; }

    public Role Role { get; set; } = null!;

    public string? Department { get; set; }

    public string? AvatarUrl { get; set; }

    public string? AvatarPublicId { get; set; }

    public AvatarSource AvatarSource { get; set; } = AvatarSource.None;

    public DateTimeOffset? AvatarUpdatedAt { get; set; }

    public UserStatus Status { get; set; } = UserStatus.PendingVerification;

    public DateTimeOffset? LastLoginAt { get; set; }

    public ICollection<OtpChallenge> OtpChallenges { get; set; } = new List<OtpChallenge>();

    public ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();

    public ICollection<ExternalLogin> ExternalLogins { get; set; } = new List<ExternalLogin>();

    public ICollection<UserPosition> UserPositions { get; set; } = new List<UserPosition>();

    public ICollection<UserPosition> AssignedUserPositions { get; set; } = new List<UserPosition>();

    public ICollection<AuditLog> AuditLogs { get; set; } = new List<AuditLog>();
}
