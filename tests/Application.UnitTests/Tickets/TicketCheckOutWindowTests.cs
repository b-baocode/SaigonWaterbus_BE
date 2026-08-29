using NUnit.Framework;
using SaigonWaterbus.Application.Common.Exceptions;
using SaigonWaterbus.Application.Tickets;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using Shouldly;

namespace SaigonWaterbus.Application.UnitTests.Tickets;

public class TicketCheckOutWindowTests
{
    private static readonly DateTimeOffset PlannedArrival =
        new(2030, 1, 1, 12, 0, 0, TimeSpan.FromHours(7));

    [Test]
    public void CheckOutOpensExactlyTwoMinutesBeforePlannedArrival()
    {
        var (ticket, booking, passenger, _) = BuildTicket();

        Should.Throw<ValidationException>(() =>
            TicketAttendanceWindowSupport.EnsureCanCheckOutAt(
                ticket, booking, PlannedArrival.AddMinutes(-2).AddSeconds(-1)));

        Should.NotThrow(() =>
            TicketAttendanceWindowSupport.EnsureCanCheckOutAt(
                ticket, booking, PlannedArrival.AddMinutes(-2)));

        TicketAttendanceWindowSupport.IsWithinCheckOutWindow(
            booking, passenger, PlannedArrival.AddMinutes(-2)).ShouldBeTrue();
    }

    [Test]
    public void RecordedDelayMovesCheckOutOpeningTime()
    {
        var (ticket, booking, passenger, destination) = BuildTicket();
        destination.AdjustedArrivalTime = PlannedArrival.AddMinutes(10);

        TicketAttendanceWindowSupport.IsWithinCheckOutWindow(
            booking, passenger, PlannedArrival.AddMinutes(-2)).ShouldBeFalse();
        TicketAttendanceWindowSupport.IsWithinCheckOutWindow(
            booking, passenger, PlannedArrival.AddMinutes(8)).ShouldBeTrue();

        Should.NotThrow(() =>
            TicketAttendanceWindowSupport.EnsureCanCheckOutAt(
                ticket, booking, PlannedArrival.AddMinutes(8)));
    }

    private static (Ticket Ticket, Booking Booking, BookingPassenger Passenger, TripStop Destination) BuildTicket()
    {
        var trip = new Trip
        {
            TripCode = "TR-CHECKOUT-WINDOW",
            DepartureTime = PlannedArrival.AddHours(-1),
            ArrivalTime = PlannedArrival
        };
        var origin = new TripStop
        {
            Trip = trip,
            StopOrder = 1,
            PlannedDepartureTime = trip.DepartureTime,
            StopStatus = TripStopStatuses.Departed
        };
        var destination = new TripStop
        {
            Trip = trip,
            StopOrder = 2,
            PlannedArrivalTime = PlannedArrival,
            StayDurationMinutes = 5,
            StopStatus = TripStopStatuses.Scheduled
        };
        trip.TripStops.Add(origin);
        trip.TripStops.Add(destination);

        var booking = new Booking
        {
            Trip = trip,
            TripId = trip.Id,
            BookingCode = "BK-CHECKOUT-WINDOW",
            ContactName = "Nguyen Van A",
            BookingStatus = BookingStatus.Confirmed,
            PaymentStatus = BookingPaymentStatusExtensions.PaidValue
        };
        var passenger = new BookingPassenger
        {
            Booking = booking,
            Trip = trip,
            TripId = trip.Id,
            FullName = "Nguyen Van A",
            FromStopOrder = 1,
            ToStopOrder = 2
        };
        booking.Passengers.Add(passenger);
        var ticket = new Ticket
        {
            Booking = booking,
            BookingPassenger = passenger,
            TicketCode = "TK-CHECKOUT-WINDOW",
            QrToken = "QR-CHECKOUT-WINDOW",
            TicketStatus = TicketStatus.CheckedIn,
            CheckedInAt = trip.DepartureTime
        };

        return (ticket, booking, passenger, destination);
    }
}
