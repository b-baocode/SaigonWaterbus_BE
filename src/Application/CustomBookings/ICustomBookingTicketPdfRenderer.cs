namespace SaigonWaterbus.Application.CustomBookings;

public interface ICustomBookingTicketPdfRenderer
{
    byte[] Render(CustomBookingTicketExportDto export);
}
