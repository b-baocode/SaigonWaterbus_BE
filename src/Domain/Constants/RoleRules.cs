namespace SaigonWaterbus.Domain.Constants;

public abstract class RoleRules
{
    public static readonly IReadOnlyList<string> AdministratorCreatableRoles =
        [Roles.Manager, Roles.Staff, Roles.Customer];

    public static readonly IReadOnlyList<string> ManagerCreatableRoles =
        [Roles.Customer, Roles.Staff];

    public static string DefaultRegistrationRoleCode => Roles.Customer;

    public static IReadOnlyList<string> GetCreatableRoles(string actorRoleCode) =>
        actorRoleCode switch
        {
            Roles.Administrator => AdministratorCreatableRoles,
            Roles.Manager => ManagerCreatableRoles,
            _ => []
        };

    public static IReadOnlyList<string> GetAssignableRoles(string actorRoleCode) =>
        actorRoleCode switch
        {
            Roles.Administrator => AdministratorCreatableRoles,
            Roles.Manager => ManagerCreatableRoles,
            _ => []
        };

    public static bool CanCreateUser(string actorRoleCode, string targetRoleCode) =>
        GetCreatableRoles(actorRoleCode).Contains(targetRoleCode);

    public static bool CanChangeRole(string actorRoleCode, string targetRoleCode) =>
        GetAssignableRoles(actorRoleCode).Contains(targetRoleCode);
}
