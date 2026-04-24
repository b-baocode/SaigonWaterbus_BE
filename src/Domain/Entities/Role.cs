using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Domain.Entities;

public class Role : BaseAuditableEntity
{
    public string Code { get; set; } = null!;

    public string SystemName { get; set; } = null!;

    public string DisplayName { get; set; } = null!;

    public string Description { get; set; } = null!;

    public RoleScopeType DefaultScopeType { get; set; }

    public bool IsSystem { get; set; } = true;

    public ICollection<UserRoleAssignment> UserAssignments { get; set; } = new List<UserRoleAssignment>();
}
