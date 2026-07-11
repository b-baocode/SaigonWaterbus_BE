namespace SaigonWaterbus.Application.Tickets;

/// <summary>
/// Render PDF vé điện tử cho booking thường: mỗi vé 1 trang boarding pass kèm QR check-in.
/// Bản gửi người đặt có thêm trang QR chung của booking (check-in cả nhóm);
/// bản gửi từng hành khách chỉ chứa vé của người đó (BookingQrToken = null).
/// </summary>
public interface IBookingTicketPdfRenderer
{
    byte[] Render(BookingTicketPdfExportDto export);
}

public sealed record BookingTicketPdfExportDto(
    string BookingCode,
    string? TripCode,
    string? RouteName,
    DateTimeOffset? DepartureTime,
    DateTimeOffset? ArrivalTime,
    string? FromStationName,
    string? ToStationName,
    string? BoatName,
    string? BookingQrToken,
    IReadOnlyList<BookingTicketPdfItemDto> Tickets);

public sealed record BookingTicketPdfItemDto(
    string PassengerName,
    string? SeatCode,
    string? TicketTypeName,
    string TicketCode,
    string QrToken);
