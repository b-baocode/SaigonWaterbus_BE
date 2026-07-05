namespace SaigonWaterbus.Application.CharterBookings;

public interface ICharterBookingTicketPdfRenderer
{
    byte[] Render(CharterBookingTicketExportDto export);
}
