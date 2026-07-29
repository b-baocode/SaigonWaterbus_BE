using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using SaigonWaterbus.Application.Tickets;
using SaigonWaterbus.Application.UnitTests.TestInfrastructure;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using SaigonWaterbus.Infrastructure.Data;
using Shouldly;

namespace SaigonWaterbus.Application.UnitTests.Tickets;

public class RejectTicketConcessionCommandTests
{
    private static readonly DateTimeOffset Now = new(2030, 1, 1, 9, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task RegularConcessionRejectCancelsTicket()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var staffContext = await SeatFlowTestData.SeedStaffAsync(context);
        var ticket = await SeedTicketAsync(
            context,
            RouteTypes.Regular,
            passengerType: "SENIOR",
            birthYear: 1950,
            unitPrice: 0m,
            bookingTotal: 0m,
            paidAmount: 0m);
        await AddOnBoardAssignmentAsync(context, staffContext.UserId!.Value, ticket);
        var handler = CreateHandler(context, staffContext);

        var result = await handler.Handle(
            new RejectTicketConcessionCommand(
                ticket.QrToken,
                "Khach khong chung minh du dieu kien",
                new TicketScanRequestMetadata(Note: "Gate A")),
            CancellationToken.None);

        result.Action.ShouldBe("Cancelled");
        result.RequiresAdditionalPayment.ShouldBeFalse();
        result.AdditionalAmount.ShouldBe(0m);
        result.Ticket.TicketStatus.ShouldBe(nameof(TicketStatus.Cancelled));
        result.Ticket.CanCheckIn.ShouldBeFalse();

        var savedTicket = context.Tickets.Single();
        savedTicket.TicketStatus.ShouldBe(TicketStatus.Cancelled);
        var passenger = context.Set<BookingPassenger>().Single();
        passenger.PassengerType.ShouldBe("SENIOR");
        passenger.ApprovalStatus.ShouldBe("Rejected");
        passenger.ReviewedByUserId.ShouldBe(staffContext.UserId!.Value);
        passenger.ReviewNote.ShouldNotBeNull();
        passenger.ReviewNote.ShouldContain("Khach khong chung minh");

        var scanEvent = context.TicketScanEvents.Single();
        scanEvent.Action.ShouldBe(TicketScanAction.ConcessionRejected);
        scanEvent.Result.ShouldBe(TicketScanResult.Success);
        scanEvent.TicketStatusBefore.ShouldBe(TicketStatus.Active);
        scanEvent.TicketStatusAfter.ShouldBe(TicketStatus.Cancelled);
        scanEvent.Note.ShouldNotBeNull();
        scanEvent.Note.ShouldContain("Gate A");
        scanEvent.Note.ShouldContain("Original=SENIOR");
    }

    [Test]
    public async Task SightseeingConcessionRejectConvertsPassengerToAdultAndRequiresTopUp()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var staffContext = await SeatFlowTestData.SeedStaffAsync(context);
        var ticket = await SeedTicketAsync(
            context,
            RouteTypes.SightseeingLoop,
            passengerType: "DISABLED",
            birthYear: 1990,
            unitPrice: 5_000m,
            bookingTotal: 5_000m,
            paidAmount: 5_000m);
        await AddOnBoardAssignmentAsync(context, staffContext.UserId!.Value, ticket);
        var handler = CreateHandler(context, staffContext);

        var result = await handler.Handle(
            new RejectTicketConcessionCommand(
                ticket.TicketCode,
                "Khach khong xuat trinh giay xac nhan khuyet tat"),
            CancellationToken.None);

        result.Action.ShouldBe("AdjustedToAdult");
        result.OriginalPassengerType.ShouldBe("DISABLED");
        result.CurrentPassengerType.ShouldBe("ADULT");
        result.PreviousUnitPrice.ShouldBe(5_000m);
        result.CurrentUnitPrice.ShouldBe(10_000m);
        result.AdditionalAmount.ShouldBe(5_000m);
        result.RequiresAdditionalPayment.ShouldBeTrue();
        result.BookingPaidAmount.ShouldBe(5_000m);
        result.BookingTotalAmount.ShouldBe(10_000m);
        result.BookingRemainingAmount.ShouldBe(5_000m);
        result.BookingPaymentStatus.ShouldBe("DepositPaid");
        result.Ticket.TicketStatus.ShouldBe(nameof(TicketStatus.Active));
        result.Ticket.TicketTypeCode.ShouldBe("ADULT");
        result.Ticket.CanCheckIn.ShouldBeFalse();

        var booking = context.Set<Booking>()
            .Include(x => x.Payments)
            .Single();
        booking.SubtotalAmount.ShouldBe(10_000m);
        booking.TotalAmount.ShouldBe(10_000m);
        booking.DepositAmount.ShouldBe(5_000m);
        booking.RemainingAmount.ShouldBe(5_000m);
        booking.PaymentStatus.ShouldBe("DepositPaid");
        booking.Payments.Single().Amount.ShouldBe(5_000m);

        var passenger = context.Set<BookingPassenger>().Single();
        passenger.PassengerType.ShouldBe("ADULT");
        passenger.UnitPrice.ShouldBe(10_000m);
        passenger.ApprovalStatus.ShouldBe("Approved");
        passenger.ReviewedByUserId.ShouldBe(staffContext.UserId!.Value);
    }

    private static RejectTicketConcessionCommandHandler CreateHandler(
        ApplicationDbContext context,
        TestUserContext userContext) =>
        new(
            context,
            userContext,
            new FareCalculator(context),
            new FixedTimeProvider(Now));

    private static async Task<Ticket> SeedTicketAsync(
        ApplicationDbContext context,
        string routeType,
        string passengerType,
        int birthYear,
        decimal unitPrice,
        decimal bookingTotal,
        decimal paidAmount)
    {
        var boat = new Boat
        {
            Code = $"BOAT-{Guid.NewGuid():N}"[..20],
            Name = "Waterbus Test",
            SeatCount = 1,
            NumberOfDecks = 1,
            SeatsConfigured = true
        };
        var seat = new Seat
        {
            Boat = boat,
            Code = "A1",
            Deck = 1,
            Row = "A",
            Column = 1,
            IsActive = true,
            SeatTypeCode = "CABIN"
        };
        var route = new Route
        {
            RouteCode = $"RT-{Guid.NewGuid():N}"[..20],
            RouteName = "Test Route",
            RouteType = routeType
        };
        var trip = new Trip
        {
            Route = route,
            Boat = boat,
            TripCode = $"TR-{Guid.NewGuid():N}"[..20],
            OperatingDate = new DateOnly(2030, 1, 1),
            DepartureTime = Now.AddMinutes(10),
            ArrivalTime = Now.AddHours(1),
            CapacitySnapshot = 1
        };
        var tripSeat = new TripSeat
        {
            Trip = trip,
            Seat = seat,
            Status = TripSeat.StatusBooked,
            Price = 10_000m
        };
        var booking = new Booking
        {
            Trip = trip,
            TripId = trip.Id,
            BookingCode = $"BK-{Guid.NewGuid():N}"[..20],
            ContactName = "Nguyen Van A",
            ContactPhone = "0900000000",
            ContactEmail = "customer@example.test",
            BookingStatus = BookingStatus.Confirmed,
            PaymentStatus = "Paid",
            SubtotalAmount = bookingTotal,
            TotalAmount = bookingTotal,
            DepositAmount = bookingTotal,
            RemainingAmount = 0
        };
        var passenger = new BookingPassenger
        {
            Booking = booking,
            BookingId = booking.Id,
            Trip = trip,
            TripId = trip.Id,
            TripSeat = tripSeat,
            TripSeatId = tripSeat.Id,
            FullName = "Nguyen Van A",
            PhoneNumber = "0900000001",
            PassengerType = passengerType,
            BirthYear = birthYear,
            UnitPrice = unitPrice
        };
        var ticket = new Ticket
        {
            Booking = booking,
            BookingId = booking.Id,
            BookingPassenger = passenger,
            BookingPassengerId = passenger.Id,
            TicketCode = $"TK{Guid.NewGuid():N}"[..20],
            QrToken = Convert.ToHexString(Guid.NewGuid().ToByteArray()),
            TicketStatus = TicketStatus.Active,
            IssuedAt = Now.AddHours(-1)
        };

        context.AddRange(booking, passenger, ticket);
        if (paidAmount >= 0m)
        {
            context.Set<Payment>().Add(new Payment
            {
                Booking = booking,
                PaymentCode = $"PAY-{Guid.NewGuid():N}"[..20],
                Provider = paidAmount == 0m ? "System" : "PayOS",
                Amount = paidAmount,
                Currency = "VND",
                PaymentMethod = paidAmount == 0m ? "Free" : "PayOS",
                PaymentPurpose = "Full",
                PaymentStatus = "Paid",
                PaidAt = Now.AddMinutes(-30)
            });
        }

        await context.SaveChangesAsync();
        return ticket;
    }

    private static async Task AddOnBoardAssignmentAsync(
        ApplicationDbContext context,
        Guid staffUserId,
        Ticket ticket)
    {
        var boatId = ticket.BookingPassenger?.Trip?.BoatId
            ?? ticket.Booking.Trip?.BoatId
            ?? throw new InvalidOperationException("Ticket test data must have a boat.");
        context.Set<StaffWorkAssignment>().Add(new StaffWorkAssignment
        {
            StaffUserId = staffUserId,
            AssignmentType = StaffWorkAssignmentType.Boat,
            BoatId = boatId,
            WorkingDate = new DateOnly(2030, 1, 1),
            StartAt = Now.AddHours(-1),
            EndAt = Now.AddHours(2),
            Status = StaffWorkAssignmentStatus.Scheduled,
            AssignedByUserId = staffUserId,
            AssignedAt = Now.AddHours(-2)
        });
        await context.SaveChangesAsync();
    }
}
