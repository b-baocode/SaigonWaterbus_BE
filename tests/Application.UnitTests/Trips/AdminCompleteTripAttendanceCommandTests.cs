using SaigonWaterbus.Application.Trips;
using SaigonWaterbus.Application.UnitTests.TestInfrastructure;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using NUnit.Framework;
using Shouldly;

namespace SaigonWaterbus.Application.UnitTests.Trips;

public class AdminCompleteTripAttendanceCommandTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 28, 8, 0, 0, TimeSpan.FromHours(7));

    [Test]
    public async Task AdminCompletesAttendanceForEveryUsableTicketOnTheTrip()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var admin = await SeatFlowTestData.SeedAdminAsync(context);
        var trip = new Trip { TripCode = "TR-ATTENDANCE" };
        var booking = new Booking
        {
            Trip = trip,
            TripId = trip.Id,
            BookingCode = "BK-ATTENDANCE",
            ContactName = "Admin Demo",
            BookingStatus = BookingStatus.Confirmed,
            PaymentStatus = "Paid"
        };

        var activeTicket = CreateTicket(booking, trip, "TK-ACTIVE", TicketStatus.Active);
        var checkedInTicket = CreateTicket(booking, trip, "TK-CHECKED-IN", TicketStatus.CheckedIn);
        checkedInTicket.CheckedInAt = Now.AddMinutes(-10);
        var cancelledTicket = CreateTicket(booking, trip, "TK-CANCELLED", TicketStatus.Cancelled);
        context.AddRange(trip, booking, activeTicket.BookingPassenger!, checkedInTicket.BookingPassenger!,
            cancelledTicket.BookingPassenger!, activeTicket, checkedInTicket, cancelledTicket);
        await context.SaveChangesAsync();

        var result = await new AdminCompleteTripAttendanceCommandHandler(
                context,
                admin,
                new FixedTimeProvider(Now))
            .Handle(new AdminCompleteTripAttendanceCommand(trip.Id), CancellationToken.None);

        result.TotalTickets.ShouldBe(3);
        result.CheckedInCount.ShouldBe(1);
        result.CheckedOutCount.ShouldBe(2);
        result.SkippedCount.ShouldBe(1);
        result.CompletedBookingCount.ShouldBe(1);
        activeTicket.TicketStatus.ShouldBe(TicketStatus.CheckedOut);
        activeTicket.CheckedInAt.ShouldBe(Now);
        activeTicket.CheckedOutAt.ShouldBe(Now);
        checkedInTicket.TicketStatus.ShouldBe(TicketStatus.CheckedOut);
        checkedInTicket.CheckedOutAt.ShouldBe(Now);
        cancelledTicket.TicketStatus.ShouldBe(TicketStatus.Cancelled);
        booking.BookingStatus.ShouldBe(BookingStatus.Completed);
        context.TicketScanEvents.Count().ShouldBe(3);
    }

    private static Ticket CreateTicket(Booking booking, Trip trip, string code, TicketStatus status)
    {
        var passenger = new BookingPassenger
        {
            Booking = booking,
            BookingId = booking.Id,
            Trip = trip,
            TripId = trip.Id,
            FullName = code,
            PassengerType = "ADULT"
        };
        return new Ticket
        {
            Booking = booking,
            BookingId = booking.Id,
            BookingPassenger = passenger,
            BookingPassengerId = passenger.Id,
            TicketCode = code,
            QrToken = $"QR-{code}",
            TicketStatus = status,
            IssuedAt = Now
        };
    }
}
