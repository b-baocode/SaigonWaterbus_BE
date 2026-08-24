using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Application.Payments;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Application.CharterBookings;

internal static class CharterBookingETicketSupport
{
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
        CancellationToken cancellationToken)
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

        var notification = BuildETicketNotification(booking, payment, ticketResult.Tickets);
        await paymentNotificationSender.SendCharterETicketsAsync(notification, cancellationToken);
    }

    public static ETicketNotification BuildETicketNotification(
        Booking booking,
        Payment payment,
        IReadOnlyList<Ticket> tickets)
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
                    SeatCode: null,
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
            Attachments: null,
            Legs: null);
    }
}
