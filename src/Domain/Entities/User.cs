using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Domain.Entities;

public class User : BaseAuditableEntity
{
    public string? UserCode { get; set; }

    public string FullName { get; set; } = null!;

    public DateOnly DateOfBirth { get; set; }

    public string PhoneNumber { get; set; } = null!;

    public string NormalizedPhoneNumber { get; set; } = null!;

    public string Email { get; set; } = null!;

    public string NormalizedEmail { get; set; } = null!;

    public string PasswordHash { get; set; } = null!;

    public UserStatus Status { get; set; } = UserStatus.PendingVerification;

    public DateTimeOffset? EmailVerifiedAt { get; set; }

    public DateTimeOffset? LastLoginAt { get; set; }

    public ICollection<UserRoleAssignment> RoleAssignments { get; set; } = new List<UserRoleAssignment>();

    public ICollection<OtpChallenge> OtpChallenges { get; set; } = new List<OtpChallenge>();

    public ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();

    public ICollection<ExternalLogin> ExternalLogins { get; set; } = new List<ExternalLogin>();
}
