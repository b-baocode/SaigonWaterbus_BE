namespace SaigonWaterbus.Domain.Constants;

public static class Roles
{
    public const string CustomerCode = "CU01";
    public const string ManagerCode = "MG01";
    public const string AdminCode = "AD01";
    public const string StaffCode = "ST01";

    public const string CustomerSystemName = "CUSTOMER";
    public const string ManagerSystemName = "MANAGER";
    public const string AdminName = "ADMIN";
    public const string StaffSystemName = "STAFF";

    public const string Administrator = AdminCode;
    public const string Manager = ManagerCode;
    public const string Staff = StaffCode;
    public const string Customer = CustomerCode;

    public static IReadOnlyCollection<RoleDefinition> BuiltIn { get; } =
    [
        new(CustomerCode, CustomerSystemName, "Customer"),
        new(ManagerCode, ManagerSystemName, "Manager"),
        new(AdminCode, AdminName, "Admin"),
        new(StaffCode, StaffSystemName, "Staff")
    ];
}

public sealed record RoleDefinition(string Code, string SystemName, string DisplayName);
