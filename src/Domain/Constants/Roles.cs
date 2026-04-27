namespace SaigonWaterbus.Domain.Constants;

public static class Roles
{
    public const string CustomerCode = "CU01";
    public const string ManagerCode = "MG01";
    public const string AdminSystemCode = "AD01";
    public const string StaffCode = "ST01";

    public const string CustomerSystemName = "CUSTOMER";
    public const string ManagerSystemName = "MANAGER";
    public const string AdminSystemName = "ADMIN_SYSTEM";
    public const string StaffSystemName = "STAFF";

    public const string Administrator = AdminSystemCode;
    public const string Manager = ManagerCode;
    public const string Staff = StaffCode;
    public const string Customer = CustomerCode;

    public static IReadOnlyCollection<RoleDefinition> BuiltIn { get; } =
    [
        new(CustomerCode, CustomerSystemName, "Customer"),
        new(ManagerCode, ManagerSystemName, "Manager"),
        new(AdminSystemCode, AdminSystemName, "Admin System"),
        new(StaffCode, StaffSystemName, "Staff")
    ];
}

public sealed record RoleDefinition(string Code, string SystemName, string DisplayName);
