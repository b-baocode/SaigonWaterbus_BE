namespace SaigonWaterbus.Application.Common.Interfaces;

public interface IPaymentNotificationSender
{
    Task SendPaymentSucceededAsync(PaymentSucceededNotification notification, CancellationToken cancellationToken);

    Task SendBoardingPassAsync(BoardingPassNotification notification, CancellationToken cancellationToken);

    /// <summary>
    /// Gửi email vé điện tử cho booking thường sau khi thanh toán đủ:
    /// QR chung của booking + QR riêng của từng hành khách.
    /// </summary>
    Task SendETicketsAsync(ETicketNotification notification, CancellationToken cancellationToken);
}

public sealed record PaymentSucceededNotification(
    string Email,
    string ContactName,
    string ContactPhone,
    string BookingCode,
    string BookingType,
    DateTimeOffset BookingCreatedAt,
    string PaymentCode,
    string PaymentPurpose,
    decimal PaymentAmount,
    string Currency,
    decimal BookingTotalAmount,
    string BookingPaymentStatus,
    decimal DepositAmount,
    decimal RemainingAmount,
    DateTimeOffset PaidAt,
    bool IsFullyPaid,
    DateOnly? DepartureDate,
    TimeOnly? StartTime,
    string? RentalUnit,
    int DurationValue,
    int PassengerCount,
    string? BoatName,
    string? FromStationName,
    string? FromStationAddress,
    string? ToStationName,
    string? ToStationAddress,
    IReadOnlyList<PaymentNotificationStop> Stops,
    IReadOnlyList<PaymentNotificationVessel>? Vessels = null,
    PaymentNotificationInsurance? Insurance = null,
    DateTimeOffset? RemainingPaymentDueAt = null,
    DateTimeOffset? PassengerListDueAt = null);

public sealed record PaymentNotificationStop(
    string Name,
    string? Description,
    int StayDurationMinutes);

public sealed record PaymentNotificationVessel(
    string Name,
    int? SeatCount = null,
    int? Order = null);

public sealed record PaymentNotificationInsurance(
    string PackageName,
    int InsuredSeatCount,
    decimal TotalFee,
    string Currency);

public sealed record BoardingPassNotification(
    PaymentSucceededNotification Booking,
    string TicketCode,
    string QrToken,
    string? QrImageUrl = null,
    string? PdfUrl = null,
    IReadOnlyList<EmailAttachment>? Attachments = null,
    string? PassengerName = null);

public sealed record EmailAttachment(
    string Name,
    string ContentType,
    byte[] Content);

// Vé khứ hồi: Legs chứa từng chiều (đi/về); các field phẳng giữ thông tin chiều đi để template
// một chiều cũ vẫn hoạt động. Legs = null với booking một chiều.
public sealed record ETicketNotification(
    PaymentSucceededNotification Booking,
    string? BookingQrToken,
    string? TripCode,
    string? RouteName,
    DateTimeOffset? DepartureTime,
    DateTimeOffset? ArrivalTime,
    string? FromStationName,
    string? ToStationName,
    IReadOnlyList<ETicketPassenger> Tickets,
    IReadOnlyList<EmailAttachment>? Attachments = null,
    IReadOnlyList<ETicketLeg>? Legs = null);

public sealed record ETicketLeg(
    string? TripCode,
    string? RouteName,
    DateTimeOffset? DepartureTime,
    DateTimeOffset? ArrivalTime,
    string? FromStationName,
    string? ToStationName,
    IReadOnlyList<ETicketPassenger> Tickets);

// FromStationName/ToStationName: chặng của riêng hành khách (trip Regular bán ghế theo chặng);
// null = đi cả tuyến (dữ liệu cũ, sightseeing) → hiển thị theo trạm đầu/cuối của leg.
public sealed record ETicketPassenger(
    string PassengerName,
    string? SeatCode,
    string? TicketTypeName,
    string TicketCode,
    string QrToken,
    string? Email,
    string? FromStationName = null,
    string? ToStationName = null);
