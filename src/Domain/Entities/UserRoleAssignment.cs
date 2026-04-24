using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Domain.Entities;

public class UserRoleAssignment : BaseAuditableEntity
{
    public int UserId { get; set; }

    public int RoleId { get; set; }

    public RoleScopeType ScopeType { get; set; }

    public int? ScopeEntityId { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTimeOffset AssignedAt { get; set; }

    public int? AssignedByUserId { get; set; }

    public User User { get; set; } = null!;

    public Role Role { get; set; } = null!;
}
