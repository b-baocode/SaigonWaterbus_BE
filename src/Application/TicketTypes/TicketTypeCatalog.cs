namespace SaigonWaterbus.Application.TicketTypes;

public static class TicketTypeCatalog
{
    public const string CustomBookingTicketTypeCode = "CUSTOM_BOOKING";
    public const string CustomBookingTicketTypeName = "Vé thuê tàu";

    public static string NormalizeCode(string value) => value.Trim().ToUpperInvariant();
}
