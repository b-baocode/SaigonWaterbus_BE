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
    public void CheckOutOpensExactlyThreeMinutesBeforePlannedArrival()
    {
        var (ticket, booking, passenger, _) = BuildTicket();

        Should.Throw<ValidationException>(() =>
            TicketAttendanceWindowSupport.EnsureCanCheckOutAt(
                ticket, booking, PlannedArrival.AddMinutes(-3).AddSeconds(-1)));

        Should.NotThrow(() =>
            TicketAttendanceWindowSupport.EnsureCanCheckOutAt(
                ticket, booking, PlannedArrival.AddMinutes(-3)));

        TicketAttendanceWindowSupport.IsWithinCheckOutWindow(
            booking, passenger, PlannedArrival.AddMinutes(-3)).ShouldBeTrue();
    }

    [Test]
    public void RecordedDelayMovesCheckOutOpeningTime()
    {
        var (ticket, booking, passenger, destination) = BuildTicket();
        destination.AdjustedArrivalTime = PlannedArrival.AddMinutes(10);

        TicketAttendanceWindowSupport.IsWithinCheckOutWindow(
            booking, passenger, PlannedArrival.AddMinutes(-3)).ShouldBeFalse();
        TicketAttendanceWindowSupport.IsWithinCheckOutWindow(
            booking, passenger, PlannedArrival.AddMinutes(7)).ShouldBeTrue();

        Should.NotThrow(() =>
            TicketAttendanceWindowSupport.EnsureCanCheckOutAt(
                ticket, booking, PlannedArrival.AddMinutes(7)));
    }

    [Test]
    public void CharterWithoutLinkedTripOpensCheckOutThreeMinutesBeforeArrival()
    {
        var arrival = new DateTimeOffset(2030, 1, 1, 8, 23, 0, TimeSpan.FromHours(7));
        var booking = new Booking
        {
            BookingType = Booking.CharterBookingType,
            DepartureDate = new DateOnly(2030, 1, 1),
            StartTime = new TimeOnly(7, 23),
            RentalUnit = BoatRentalUnit.Hour,
            DurationValue = 1
        };
        var ticket = new Ticket { Booking = booking };

        Should.Throw<ValidationException>(() =>
            TicketAttendanceWindowSupport.EnsureCanCheckOutAt(
                ticket, booking, arrival.AddMinutes(-3).AddSeconds(-1)));
        Should.NotThrow(() =>
            TicketAttendanceWindowSupport.EnsureCanCheckOutAt(
                ticket, booking, arrival.AddMinutes(-3)));
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
