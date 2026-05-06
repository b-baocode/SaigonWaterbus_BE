namespace SaigonWaterbus.Domain.Constants;

public static class StaffPositions
{
    public const string BoatDriverCode = "BD";
    public const string CustomerSupportCode = "CS";
    public const string TicketCheckerCode = "TC";
    public const string TicketPrinterCode = "TP";
    public const string MaintenanceStaffCode = "MT";

    public static IReadOnlyCollection<StaffPositionDefinition> BuiltIn { get; } =
    [
        new(BoatDriverCode, "BOAT_DRIVER", "Boat Driver"),
        new(CustomerSupportCode, "CUSTOMER_SUPPORT", "Customer Support"),
        new(TicketCheckerCode, "TICKET_CHECKER", "Ticket Checker"),
        new(TicketPrinterCode, "TICKET_PRINTER", "Ticket Printer"),
        new(MaintenanceStaffCode, "MAINTENANCE_STAFF", "Maintenance Staff")
    ];
}

public sealed record StaffPositionDefinition(string Code, string SystemName, string DisplayName);
