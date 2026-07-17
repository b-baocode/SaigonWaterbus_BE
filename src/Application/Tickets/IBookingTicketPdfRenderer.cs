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

// Vé khứ hồi: Legs chứa từng chiều (đi/về); các field phẳng giữ thông tin chiều đi để layout
// một chiều cũ vẫn hoạt động. Legs = null với booking một chiều.
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
    IReadOnlyList<BookingTicketPdfItemDto> Tickets,
    IReadOnlyList<BookingTicketPdfLegDto>? Legs = null);

public sealed record BookingTicketPdfLegDto(
    string? TripCode,
    string? RouteName,
    DateTimeOffset? DepartureTime,
    DateTimeOffset? ArrivalTime,
    string? FromStationName,
    string? ToStationName,
    string? BoatName,
    IReadOnlyList<BookingTicketPdfItemDto> Tickets);

// DepartureTime = giờ tàu rời bến lên của hành khách (theo chặng); null → dùng giờ chuyến.
public sealed record BookingTicketPdfItemDto(
    string PassengerName,
    string? SeatCode,
    string? TicketTypeName,
    string TicketCode,
    string QrToken,
    string? FromStationName = null,
    string? ToStationName = null,
    DateTimeOffset? DepartureTime = null);
