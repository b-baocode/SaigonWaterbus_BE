using NUnit.Framework;
using SaigonWaterbus.Application.Common.Exceptions;
using SaigonWaterbus.Application.Tickets;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using Shouldly;

namespace SaigonWaterbus.Application.UnitTests.Tickets;

public class TicketCheckInWindowTests
{
    private static readonly DateTimeOffset PlannedDeparture =
        new(2030, 1, 1, 12, 0, 0, TimeSpan.FromHours(7));

    [Test]
    public void CheckInOpensExactlyTenMinutesBeforePlannedDeparture()
    {
        var (ticket, booking, passenger, origin) = BuildTicket();

        Should.Throw<ValidationException>(() =>
            TicketAttendanceWindowSupport.EnsureCanCheckInAt(
                ticket, booking, PlannedDeparture.AddMinutes(-10).AddSeconds(-1)));

        Should.NotThrow(() =>
            TicketAttendanceWindowSupport.EnsureCanCheckInAt(
                ticket, booking, PlannedDeparture.AddMinutes(-10)));

        TicketAttendanceWindowSupport.IsWithinCheckInWindow(
            booking, passenger, PlannedDeparture.AddMinutes(-10)).ShouldBeTrue();
        origin.ActualArrivalTime.ShouldBe(PlannedDeparture.AddMinutes(-20));
    }

    [Test]
    public void RecordedDelayMovesCheckInOpeningTime()
    {
        var (ticket, booking, passenger, origin) = BuildTicket();
        origin.AdjustedDepartureTime = PlannedDeparture.AddMinutes(10);

        TicketAttendanceWindowSupport.IsWithinCheckInWindow(
            booking, passenger, PlannedDeparture.AddMinutes(-10)).ShouldBeFalse();
        TicketAttendanceWindowSupport.IsWithinCheckInWindow(
            booking, passenger, PlannedDeparture).ShouldBeTrue();

        Should.NotThrow(() =>
            TicketAttendanceWindowSupport.EnsureCanCheckInAt(
                ticket, booking, PlannedDeparture));
    }

    [Test]
    public void CheckInClosesExactlyTwoMinutesAfterActualDeparture()
    {
        var (ticket, booking, passenger, origin) = BuildTicket();
        origin.ActualDepartureTime = PlannedDeparture;
        origin.StopStatus = TripStopStatuses.Departed;

        Should.NotThrow(() =>
            TicketAttendanceWindowSupport.EnsureCanCheckInAt(
                ticket, booking, PlannedDeparture.AddMinutes(2)));
        Should.Throw<ValidationException>(() =>
            TicketAttendanceWindowSupport.EnsureCanCheckInAt(
                ticket, booking, PlannedDeparture.AddMinutes(2).AddSeconds(1)));

        TicketAttendanceWindowSupport.IsWithinCheckInWindow(
            booking, passenger, PlannedDeparture.AddMinutes(2)).ShouldBeTrue();
        TicketAttendanceWindowSupport.IsWithinCheckInWindow(
            booking, passenger, PlannedDeparture.AddMinutes(2).AddSeconds(1)).ShouldBeFalse();
    }

    [Test]
    public void CheckInUsesAdjustedDepartureWhenDepartureIsRecordedWithoutActualTime()
    {
        var (ticket, booking, passenger, origin) = BuildTicket();
        origin.StopStatus = TripStopStatuses.Departed;
        origin.ActualDepartureTime = null;
        origin.AdjustedDepartureTime = PlannedDeparture.AddMinutes(5);

        Should.NotThrow(() =>
            TicketAttendanceWindowSupport.EnsureCanCheckInAt(
                ticket, booking, PlannedDeparture.AddMinutes(7)));
        Should.Throw<ValidationException>(() =>
            TicketAttendanceWindowSupport.EnsureCanCheckInAt(
                ticket, booking, PlannedDeparture.AddMinutes(7).AddSeconds(1)));

        TicketAttendanceWindowSupport.IsWithinCheckInWindow(
            booking, passenger, PlannedDeparture.AddMinutes(7)).ShouldBeTrue();
        TicketAttendanceWindowSupport.IsWithinCheckInWindow(
            booking, passenger, PlannedDeparture.AddMinutes(7).AddSeconds(1)).ShouldBeFalse();
    }

    [Test]
    public void CharterWithoutLinkedTripUsesTenMinutesBeforeAndTwoMinutesAfterDeparture()
    {
        var departure = new DateTimeOffset(2030, 1, 1, 7, 23, 0, TimeSpan.FromHours(7));
        var booking = BuildCharterBooking();
        var ticket = new Ticket { Booking = booking };

        Should.Throw<ValidationException>(() =>
            TicketAttendanceWindowSupport.EnsureCanCheckInAt(
                ticket, booking, departure.AddMinutes(-10).AddSeconds(-1)));
        Should.NotThrow(() =>
            TicketAttendanceWindowSupport.EnsureCanCheckInAt(
                ticket, booking, departure.AddMinutes(-10)));
        Should.NotThrow(() =>
            TicketAttendanceWindowSupport.EnsureCanCheckInAt(
                ticket, booking, departure.AddMinutes(2)));
        Should.Throw<ValidationException>(() =>
            TicketAttendanceWindowSupport.EnsureCanCheckInAt(
                ticket, booking, departure.AddMinutes(2).AddSeconds(1)));
    }

    private static (Ticket Ticket, Booking Booking, BookingPassenger Passenger, TripStop Origin) BuildTicket()
    {
        var trip = new Trip
        {
            TripCode = "TR-CHECKIN-WINDOW",
            DepartureTime = PlannedDeparture,
            ArrivalTime = PlannedDeparture.AddHours(1)
        };
        var origin = new TripStop
        {
            Trip = trip,
            StopOrder = 1,
            PlannedDepartureTime = PlannedDeparture,
            ActualArrivalTime = PlannedDeparture.AddMinutes(-20),
            StayDurationMinutes = 20,
            StopStatus = TripStopStatuses.Arrived
        };
        var destination = new TripStop
        {
            Trip = trip,
            StopOrder = 2,
            PlannedArrivalTime = trip.ArrivalTime,
            StopStatus = TripStopStatuses.Scheduled
        };
        trip.TripStops.Add(origin);
        trip.TripStops.Add(destination);

        var booking = new Booking
        {
            Trip = trip,
            TripId = trip.Id,
            BookingCode = "BK-CHECKIN-WINDOW",
            ContactName = "Nguyen Van A"
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
        var ticket = new Ticket
        {
            Booking = booking,
            BookingPassenger = passenger,
            TicketCode = "TK-CHECKIN-WINDOW",
            QrToken = "QR-CHECKIN-WINDOW"
        };

        return (ticket, booking, passenger, origin);
    }

    private static Booking BuildCharterBooking() => new()
    {
        BookingType = Booking.CharterBookingType,
        DepartureDate = new DateOnly(2030, 1, 1),
        StartTime = new TimeOnly(7, 23),
        RentalUnit = BoatRentalUnit.Hour,
        DurationValue = 1
    };
}
