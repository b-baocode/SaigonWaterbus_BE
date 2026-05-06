namespace SaigonWaterbus.Domain.Constants;

public static class StaffPositions
{
    public const string TicketSellerCode = "TS";
    public const string OperatorCode = "OP";
    public const string CustomerSupportCode = "CS";

    public static IReadOnlyCollection<StaffPositionDefinition> BuiltIn { get; } =
    [
        new(TicketSellerCode, "TICKET_SELLER", "Ticket Seller"),
        new(OperatorCode, "OPERATOR", "Operator"),
        new(CustomerSupportCode, "CUSTOMER_SUPPORT", "Customer Support")
    ];
}

public sealed record StaffPositionDefinition(string Code, string SystemName, string DisplayName);
