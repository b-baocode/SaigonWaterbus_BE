namespace SaigonWaterbus.Application.TicketTypes;

public static class TicketTypeCatalog
{
    public const string CharterBookingTicketTypeCode = "CUSTOM_BOOKING";
    public const string CharterBookingTicketTypeName = "Vé thuê tàu";

    public static string NormalizeCode(string value) => value.Trim().ToUpperInvariant();
}
