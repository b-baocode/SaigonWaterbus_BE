using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Domain.Constants;

public static class Roles
{
    public const string CustomerCode = "CU01";
    public const string ManagerCode = "MG01";
    public const string AdminSystemCode = "AD01";
    public const string StaffCode = "ST01";
    public const string OperatorSystemCode = "OP01";

    public const string CustomerSystemName = "CUSTOMER";
    public const string ManagerSystemName = "MANAGER";
    public const string AdminSystemName = "ADMIN_SYSTEM";
    public const string StaffSystemName = "STAFF";
    public const string OperatorSystemName = "OPERATOR_SYSTEM";

    public static IReadOnlyCollection<RoleDefinition> BuiltIn { get; } =
    [
        new(CustomerCode, CustomerSystemName, "Customer",
            "Create and manage personal account, search schedules, book tickets, pay online, receive QR tickets, review services, and manage points and booking history.",
            RoleScopeType.Self),
        new(ManagerCode, ManagerSystemName, "Manager",
            "Manage staff accounts, assigned stations or fleets, monitor ticket sales, promotions, reports, dashboards, and handle operational incidents.",
            RoleScopeType.Station),
        new(AdminSystemCode, AdminSystemName, "Admin System",
            "Manage user statuses, stations, boats, routes, schedules, pricing, promotions, ticket types, and system-wide dashboards.",
            RoleScopeType.Global),
        new(StaffCode, StaffSystemName, "Staff",
            "Verify QR tickets, update boarding status, sell tickets at stations, and monitor trip passenger lists.",
            RoleScopeType.Station),
        new(OperatorSystemCode, OperatorSystemName, "Operator System",
            "Respond to customer inquiries and complaints, and manage tourism and attraction content.",
            RoleScopeType.Global)
    ];
}

public sealed record RoleDefinition(
    string Code,
    string SystemName,
    string DisplayName,
    string Description,
    RoleScopeType DefaultScopeType);
