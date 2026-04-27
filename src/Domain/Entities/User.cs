using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Domain.Entities;

public class User : BaseAuditableEntity
{
    public string? UserCode { get; set; }

    public string FullName { get; set; } = null!;

    public DateOnly? DateOfBirth { get; set; }

    public string? PhoneNumber { get; set; }

    public string? NormalizedPhoneNumber { get; set; }

    public string Email { get; set; } = null!;

    public string NormalizedEmail { get; set; } = null!;

    public string? PasswordHash { get; set; }

    public int RoleId { get; set; }

    public Role Role { get; set; } = null!;

    public string? Department { get; set; }

    public UserStatus Status { get; set; } = UserStatus.PendingVerification;

    public DateTimeOffset? EmailVerifiedAt { get; set; }

    public DateTimeOffset? LastLoginAt { get; set; }

    public ICollection<OtpChallenge> OtpChallenges { get; set; } = new List<OtpChallenge>();

    public ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();

    public ICollection<ExternalLogin> ExternalLogins { get; set; } = new List<ExternalLogin>();
}
