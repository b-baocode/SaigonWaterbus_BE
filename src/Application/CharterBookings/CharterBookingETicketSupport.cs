using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Application.Payments;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Application.CharterBookings;

internal static class CharterBookingETicketSupport
{
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
