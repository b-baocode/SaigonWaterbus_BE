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
    IReadOnlyList<PaymentNotificationStop> Stops);

public sealed record PaymentNotificationStop(
    string Name,
    string? Description,
    int StayDurationMinutes);

public sealed record BoardingPassNotification(
    PaymentSucceededNotification Booking,
    string TicketCode,
    string QrToken,
    string? QrImageUrl = null,
    string? PdfUrl = null,
    IReadOnlyList<EmailAttachment>? Attachments = null);

public sealed record EmailAttachment(
    string Name,
    string ContentType,
    byte[] Content);

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
    IReadOnlyList<EmailAttachment>? Attachments = null);

public sealed record ETicketPassenger(
    string PassengerName,
    string? SeatCode,
    string? TicketTypeName,
    string TicketCode,
    string QrToken,
    string? Email);
