using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Domain.Entities;

public class User : BaseAuditableEntity
{
    public string? UserCode { get; set; }

    public string FullName { get; set; } = null!;

    public DateOnly? DateOfBirth { get; set; }

    public string? Gender { get; set; }

    public string? Nationality { get; set; }

    public string? PhoneNumber { get; set; }

    public string? NormalizedPhoneNumber { get; set; }

    public DateTimeOffset? PhoneVerifiedAt { get; set; }

    public string? Email { get; set; }

    public string? NormalizedEmail { get; set; }

    public string? PasswordHash { get; set; }

    public Guid RoleId { get; set; }

    public Role Role { get; set; } = null!;

    public StaffType? StaffType { get; set; }

    public string? AvatarUrl { get; set; }

    public string? AvatarPublicId { get; set; }

    public AvatarSource AvatarSource { get; set; } = AvatarSource.None;

    public DateTimeOffset? AvatarUpdatedAt { get; set; }

    public UserStatus Status { get; set; } = UserStatus.PendingVerification;

    public DateTimeOffset? LastLoginAt { get; set; }

    public int FailedLoginAttemptCount { get; set; }

    public DateTimeOffset? FailedLoginWindowStartedAt { get; set; }

    public int PointBalance { get; set; } = 0;

    public ICollection<OtpChallenge> OtpChallenges { get; set; } = new List<OtpChallenge>();

    public ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();

    public ICollection<UserStationAssignment> StationAssignments { get; set; } = new List<UserStationAssignment>();

    public ICollection<UserStationAssignment> AssignedStationAssignments { get; set; } = new List<UserStationAssignment>();

    public ICollection<StaffWorkAssignment> StaffWorkAssignments { get; set; } = new List<StaffWorkAssignment>();

    public ICollection<StaffWorkAssignment> AssignedStaffWorkAssignments { get; set; } = new List<StaffWorkAssignment>();
}
