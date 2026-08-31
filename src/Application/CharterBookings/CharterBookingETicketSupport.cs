using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Application.Payments;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Application.CharterBookings;

internal static class CharterBookingETicketSupport
{
    private const string PassengerAddInsurancePurpose = "PassengerAddInsurance";

    /// <summary>
    /// Gửi email mã vé charter khi khách đã trả đủ 100% và có danh sách hành khách đã duyệt.
    ///
    ///   - <c>RemainingAmount &lt; 0</c> (chưa trả đủ) → skip.
    ///   - Không có hành khách đã duyệt → skip (sẽ gửi sau khi khách import danh sách).
    ///   - Đã có vé cho tất cả hành khách → vẫn gửi lại email mã vé (idempotent).
    /// </summary>
    public static async Task SendETicketsIfFullyPaidAsync(
        IApplicationDbContext context,
        TimeProvider timeProvider,
        IPaymentNotificationSender paymentNotificationSender,
        Booking booking,
        Payment payment,
        CancellationToken cancellationToken,
        ICharterBookingTicketPdfRenderer? ticketPdfRenderer = null)
    {
        if (booking.RemainingAmount > 0)
        {
            return;
        }

        var ticketResult = await CharterBookingTicketSupport.EnsurePassengerTicketsAsync(
            context,
            booking,
            timeProvider,
            cancellationToken);
        if (ticketResult is null || ticketResult.Tickets.Count == 0)
        {
            return;
        }

        if (ticketResult.CreatedTickets.Count > 0)
        {
            // Có vé mới phát hành → cần save để QR token được persist trước khi gửi email.
            await context.SaveChangesAsync(cancellationToken);
        }

        var attachments = BuildBundleAttachmentsIfNeeded(booking, payment, ticketResult.Tickets, ticketPdfRenderer);

        var notification = BuildETicketNotification(
            booking,
            payment,
            ticketResult.Tickets,
            attachments);
        await paymentNotificationSender.SendCharterETicketsAsync(notification, cancellationToken);
    }

    /// <summary>
    /// Every charter e-ticket includes a PDF bundle so the recipient can keep the
    /// boarding pass offline. For passenger-add insurance it includes old and new tickets.
    /// </summary>
    private static IReadOnlyList<EmailAttachment>? BuildBundleAttachmentsIfNeeded(
        Booking booking,
        Payment payment,
        IReadOnlyList<Ticket> tickets,
        ICharterBookingTicketPdfRenderer? ticketPdfRenderer)
    {
        if (ticketPdfRenderer is null)
        {
            return null;
        }

        var export = CharterBookingTicketExportSupport.ToDto(booking, ticketIds: null);
        var pdfBytes = ticketPdfRenderer.Render(export);

        return
        [
            new EmailAttachment(
                $"{SanitizeFileName(booking.BookingCode)}-tickets.pdf",
                "application/pdf",
                pdfBytes)
        ];
    }

    private static string SanitizeFileName(string value)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var safeValue = new string(value.Select(x => invalidChars.Contains(x) ? '-' : x).ToArray());
        return string.IsNullOrWhiteSpace(safeValue) ? "all-tickets" : safeValue;
    }

    public static ETicketNotification BuildETicketNotification(
        Booking booking,
        Payment payment,
        IReadOnlyList<Ticket> tickets,
        IReadOnlyList<EmailAttachment>? attachments = null)
    {
        var passengers = booking.Passengers
            .Where(CharterBookingPassengerSupport.IsApproved)
            .OrderBy(x => x.FullName)
            .ToList();

        var eTicketPassengers = passengers
            .Select(passenger =>
            {
                var ticket = tickets.FirstOrDefault(t => t.BookingPassengerId == passenger.Id);
                return new ETicketPassenger(
                    PassengerName: passenger.FullName,
                    SeatCode: passenger.TripSeat?.Seat?.Code,
                    TicketTypeName: CharterBookingPassengerSupport.GetPassengerTypeName(passenger.PassengerType),
                    TicketCode: ticket?.TicketCode ?? string.Empty,
                    QrToken: ticket?.QrToken ?? string.Empty,
                    Email: passenger.Email,
                    FromStationName: booking.FromStation?.StationName,
                    ToStationName: booking.ToStation?.StationName,
                    DepartureTime: null,
                    ArrivalTime: null,
                    IsLapInfant: false,
                    CompanionPassengerId: null,
                    CompanionPassengerName: null,
                    UsesCompanionTicket: false);
            })
            .ToList();

        var bookingNotification = PaymentSupport.CreatePaymentSucceededNotification(booking, payment);

        return new ETicketNotification(
            Booking: bookingNotification,
            BookingQrToken: booking.CharterBookingQrToken,
            TripCode: null,
            RouteName: booking.CharterRoute?.RouteName,
            DepartureTime: booking.DepartureDate.HasValue && booking.StartTime.HasValue
                ? booking.DepartureDate.Value.ToDateTime(booking.StartTime.Value, DateTimeKind.Utc)
                : null,
            ArrivalTime: null,
            FromStationName: booking.FromStation?.StationName,
            ToStationName: booking.ToStation?.StationName,
            Tickets: eTicketPassengers,
            Attachments: attachments,
            Legs: null);
    }
}
