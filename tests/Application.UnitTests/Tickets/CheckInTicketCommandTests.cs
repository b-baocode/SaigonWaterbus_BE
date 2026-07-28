using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using SaigonWaterbus.Application.Common.Exceptions;
using SaigonWaterbus.Application.Tickets;
using SaigonWaterbus.Application.UnitTests.TestInfrastructure;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using Shouldly;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.UnitTests.Tickets;

public class CheckInTicketCommandTests
{
    [Test]
    public async Task StaffCanCheckInRegularBookingTicket()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var staffContext = await SeatFlowTestData.SeedStaffAsync(context);
        var now = new DateTimeOffset(2030, 1, 1, 9, 0, 0, TimeSpan.Zero);
        var ticket = await SeedRegularBookingTicketAsync(context);
        await AddOnBoardAssignmentAsync(context, staffContext.UserId!.Value, ticket, now.AddHours(-1), now.AddHours(1));
        var handler = new CheckInTicketCommandHandler(
            context,
            staffContext,
            new FixedTimeProvider(now));

        var result = await handler.Handle(new CheckInTicketCommand(ticket.QrToken), CancellationToken.None);

        result.TicketStatus.ShouldBe(nameof(TicketStatus.CheckedIn));
        result.CanCheckIn.ShouldBeFalse();
        result.CanCheckOut.ShouldBeTrue();
        result.CheckedInAt.ShouldBe(now);
        result.CheckedInByUserId.ShouldBe(staffContext.UserId!.Value);
        result.TicketPassenger.ShouldNotBeNull();
        result.TicketPassenger.FullName.ShouldBe("Nguyen Van A");
        result.SeatCode.ShouldBe("A1");

        var savedTicket = context.Tickets.Single();
        savedTicket.TicketStatus.ShouldBe(TicketStatus.CheckedIn);
        savedTicket.CheckedInAt.ShouldBe(now);
        savedTicket.CheckedInByUserId.ShouldBe(staffContext.UserId!.Value);

        var scanEvent = context.TicketScanEvents.Single();
        scanEvent.TicketId.ShouldBe(ticket.Id);
        scanEvent.BookingId.ShouldBe(ticket.BookingId);
        scanEvent.TripId.ShouldBe(ticket.Booking.TripId);
        scanEvent.PerformedByUserId.ShouldBe(staffContext.UserId!.Value);
        scanEvent.Action.ShouldBe(TicketScanAction.CheckIn);
        scanEvent.Result.ShouldBe(TicketScanResult.Success);
        scanEvent.Source.ShouldBe(TicketScanSource.Qr);
        scanEvent.ServerTime.ShouldBe(now);
        scanEvent.TicketStatusBefore.ShouldBe(TicketStatus.Active);
        scanEvent.TicketStatusAfter.ShouldBe(TicketStatus.CheckedIn);
        scanEvent.StaffWorkAssignmentId.ShouldNotBeNull();
    }

    [Test]
    public async Task StaffCanScanSameTicketManyTimesAndUseCheckedInTicketForCheckout()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var staffContext = await SeatFlowTestData.SeedStaffAsync(context);
        var now = new DateTimeOffset(2030, 1, 1, 9, 0, 0, TimeSpan.Zero);
        var ticket = await SeedRegularBookingTicketAsync(context);
        await AddOnBoardAssignmentAsync(context, staffContext.UserId!.Value, ticket, now.AddHours(-1), now.AddHours(2));

        var firstScan = await new ScanTicketQueryHandler(
                context,
                staffContext,
                new FixedTimeProvider(now))
            .Handle(new ScanTicketQuery(ticket.QrToken), CancellationToken.None);
        var secondScan = await new ScanTicketQueryHandler(
                context,
                staffContext,
                new FixedTimeProvider(now.AddMinutes(1)))
            .Handle(new ScanTicketQuery(ticket.QrToken), CancellationToken.None);

        firstScan.TicketStatus.ShouldBe(nameof(TicketStatus.Active));
        firstScan.CanCheckIn.ShouldBeTrue();
        firstScan.CanCheckOut.ShouldBeFalse();
        secondScan.TicketStatus.ShouldBe(nameof(TicketStatus.Active));
        secondScan.CanCheckIn.ShouldBeTrue();
        secondScan.CanCheckOut.ShouldBeFalse();

        await new CheckInTicketCommandHandler(
                context,
                staffContext,
                new FixedTimeProvider(now.AddMinutes(2)))
            .Handle(new CheckInTicketCommand(ticket.QrToken), CancellationToken.None);

        var checkoutLookup = await new ScanTicketQueryHandler(
                context,
                staffContext,
                new FixedTimeProvider(now.AddMinutes(3)))
            .Handle(new ScanTicketQuery(ticket.QrToken), CancellationToken.None);

        checkoutLookup.TicketStatus.ShouldBe(nameof(TicketStatus.CheckedIn));
        checkoutLookup.CanCheckIn.ShouldBeFalse();
        checkoutLookup.CanCheckOut.ShouldBeTrue();

        var checkout = await new CheckOutTicketCommandHandler(
                context,
                staffContext,
                new FixedTimeProvider(now.AddMinutes(4)))
            .Handle(new CheckOutTicketCommand(ticket.QrToken), CancellationToken.None);

        checkout.TicketStatus.ShouldBe(nameof(TicketStatus.CheckedOut));
        checkout.CanCheckIn.ShouldBeFalse();
        checkout.CanCheckOut.ShouldBeFalse();

        var finalLookup = await new ScanTicketQueryHandler(
                context,
                staffContext,
                new FixedTimeProvider(now.AddMinutes(5)))
            .Handle(new ScanTicketQuery(ticket.QrToken), CancellationToken.None);

        finalLookup.TicketStatus.ShouldBe(nameof(TicketStatus.CheckedOut));
        finalLookup.CanCheckIn.ShouldBeFalse();
        finalLookup.CanCheckOut.ShouldBeFalse();
        context.TicketScanEvents.Count(x => x.Action == TicketScanAction.Scan).ShouldBe(4);
        context.TicketScanEvents.Count(x => x.Action == TicketScanAction.CheckIn).ShouldBe(1);
        context.TicketScanEvents.Count(x => x.Action == TicketScanAction.CheckOut).ShouldBe(1);
    }

    [Test]
    public async Task StaffWithoutActiveBoatAssignmentCannotCheckInTicket()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var staffContext = await SeatFlowTestData.SeedStaffAsync(context);
        var now = new DateTimeOffset(2030, 1, 1, 9, 0, 0, TimeSpan.Zero);
        var ticket = await SeedRegularBookingTicketAsync(context);
        var handler = new CheckInTicketCommandHandler(
            context,
            staffContext,
            new FixedTimeProvider(now));

        var ex = await Should.ThrowAsync<ValidationException>(() =>
            handler.Handle(new CheckInTicketCommand(ticket.QrToken), CancellationToken.None));

        ex.Errors["staffWorkAssignment"].Single().ShouldContain("chưa có ca OnBoard");

        var scanEvent = context.TicketScanEvents.Single();
        scanEvent.Result.ShouldBe(TicketScanResult.Failed);
        scanEvent.FailureReason.ShouldNotBeNull();
        scanEvent.FailureReason.ShouldContain("chưa có ca OnBoard");
    }

    [Test]
    public async Task CheckedInTicketCannotBeCheckedInAgain()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var staffContext = await SeatFlowTestData.SeedStaffAsync(context);
        var ticket = await SeedRegularBookingTicketAsync(
            context,
            TicketStatus.CheckedIn,
            new DateTimeOffset(2030, 1, 1, 9, 0, 0, TimeSpan.Zero));
        var handler = new CheckInTicketCommandHandler(
            context,
            staffContext,
            new FixedTimeProvider(new DateTimeOffset(2030, 1, 1, 9, 5, 0, TimeSpan.Zero)));

        var ex = await Should.ThrowAsync<ValidationException>(() =>
            handler.Handle(new CheckInTicketCommand(ticket.TicketCode), CancellationToken.None));

        ex.Errors["ticket"].Single().ShouldBe("Ve nay da duoc check-in.");

        var scanEvent = context.TicketScanEvents.Single();
        scanEvent.TicketId.ShouldBe(ticket.Id);
        scanEvent.PerformedByUserId.ShouldBe(staffContext.UserId!.Value);
        scanEvent.Action.ShouldBe(TicketScanAction.CheckIn);
        scanEvent.Result.ShouldBe(TicketScanResult.Failed);
        scanEvent.FailureReason.ShouldBe("Ve nay da duoc check-in.");
        scanEvent.TicketStatusBefore.ShouldBe(TicketStatus.CheckedIn);
        scanEvent.TicketStatusAfter.ShouldBe(TicketStatus.CheckedIn);
    }

    [Test]
    public async Task CustomerCannotCheckInTicket()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var customer = await SeedCustomerAsync(context);
        var ticket = await SeedRegularBookingTicketAsync(context);
        var handler = new CheckInTicketCommandHandler(
            context,
            new TestUserContext(customer.Id),
            new FixedTimeProvider(new DateTimeOffset(2030, 1, 1, 9, 0, 0, TimeSpan.Zero)));

        await Should.ThrowAsync<ForbiddenAccessException>(() =>
            handler.Handle(new CheckInTicketCommand(ticket.TicketCode), CancellationToken.None));
    }

    [Test]
    public async Task StaffCanCheckOutCheckedInTicket()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var staffContext = await SeatFlowTestData.SeedStaffAsync(context);
        var checkedInAt = new DateTimeOffset(2030, 1, 1, 9, 0, 0, TimeSpan.Zero);
        var checkedOutAt = new DateTimeOffset(2030, 1, 1, 10, 0, 0, TimeSpan.Zero);
        var ticket = await SeedRegularBookingTicketAsync(
            context,
            TicketStatus.CheckedIn,
            checkedInAt);
        await AddOnBoardAssignmentAsync(context, staffContext.UserId!.Value, ticket, checkedInAt.AddHours(-1), checkedOutAt.AddHours(1));
        var handler = new CheckOutTicketCommandHandler(
            context,
            staffContext,
            new FixedTimeProvider(checkedOutAt));

        var result = await handler.Handle(new CheckOutTicketCommand(ticket.QrToken), CancellationToken.None);

        result.TicketStatus.ShouldBe(nameof(TicketStatus.CheckedOut));
        result.CanCheckIn.ShouldBeFalse();
        result.CanCheckOut.ShouldBeFalse();
        result.BookingStatus.ShouldBe(nameof(BookingStatus.Completed));
        result.CheckedInAt.ShouldBe(checkedInAt);
        result.CheckedOutAt.ShouldBe(checkedOutAt);
        result.CheckedOutByUserId.ShouldBe(staffContext.UserId!.Value);

        var savedTicket = context.Tickets.Single();
        savedTicket.TicketStatus.ShouldBe(TicketStatus.CheckedOut);
        savedTicket.CheckedOutAt.ShouldBe(checkedOutAt);
        savedTicket.CheckedOutByUserId.ShouldBe(staffContext.UserId!.Value);
        context.Set<Booking>().Single().BookingStatus.ShouldBe(BookingStatus.Completed);

        var scanEvent = context.TicketScanEvents.Single();
        scanEvent.TicketId.ShouldBe(ticket.Id);
        scanEvent.PerformedByUserId.ShouldBe(staffContext.UserId!.Value);
        scanEvent.Action.ShouldBe(TicketScanAction.CheckOut);
        scanEvent.Result.ShouldBe(TicketScanResult.Success);
        scanEvent.ServerTime.ShouldBe(checkedOutAt);
        scanEvent.TicketStatusBefore.ShouldBe(TicketStatus.CheckedIn);
        scanEvent.TicketStatusAfter.ShouldBe(TicketStatus.CheckedOut);
    }

    [Test]
    public async Task BookingIsCompletedOnlyAfterEveryUsableTicketIsCheckedOut()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var staffContext = await SeatFlowTestData.SeedStaffAsync(context);
        var checkedInAt = new DateTimeOffset(2030, 1, 1, 9, 0, 0, TimeSpan.Zero);
        var firstTicket = await SeedRegularBookingTicketAsync(
            context,
            TicketStatus.CheckedIn,
            checkedInAt);
        await AddOnBoardAssignmentAsync(context, staffContext.UserId!.Value, firstTicket, checkedInAt.AddHours(-1), checkedInAt.AddHours(2));
        var secondTicket = await AddTicketToBookingAsync(
            context,
            firstTicket.Booking,
            "Tran Thi B",
            TicketStatus.CheckedIn,
            checkedInAt);
        var handler = new CheckOutTicketCommandHandler(
            context,
            staffContext,
            new FixedTimeProvider(new DateTimeOffset(2030, 1, 1, 10, 0, 0, TimeSpan.Zero)));

        var firstResult = await handler.Handle(new CheckOutTicketCommand(firstTicket.QrToken), CancellationToken.None);

        firstResult.BookingStatus.ShouldBe(nameof(BookingStatus.Confirmed));
        context.Set<Booking>().Single().BookingStatus.ShouldBe(BookingStatus.Confirmed);

        var secondResult = await handler.Handle(new CheckOutTicketCommand(secondTicket.QrToken), CancellationToken.None);

        secondResult.BookingStatus.ShouldBe(nameof(BookingStatus.Completed));
        context.Set<Booking>().Single().BookingStatus.ShouldBe(BookingStatus.Completed);
    }

    [Test]
    public async Task CancelledTicketsDoNotBlockBookingCompletionOnCheckout()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var staffContext = await SeatFlowTestData.SeedStaffAsync(context);
        var checkedInAt = new DateTimeOffset(2030, 1, 1, 9, 0, 0, TimeSpan.Zero);
        var activeTicket = await SeedRegularBookingTicketAsync(
            context,
            TicketStatus.CheckedIn,
            checkedInAt);
        await AddOnBoardAssignmentAsync(context, staffContext.UserId!.Value, activeTicket, checkedInAt.AddHours(-1), checkedInAt.AddHours(2));
        await AddTicketToBookingAsync(
            context,
            activeTicket.Booking,
            "Tran Thi B",
            TicketStatus.Cancelled);
        var handler = new CheckOutTicketCommandHandler(
            context,
            staffContext,
            new FixedTimeProvider(new DateTimeOffset(2030, 1, 1, 10, 0, 0, TimeSpan.Zero)));

        var result = await handler.Handle(new CheckOutTicketCommand(activeTicket.QrToken), CancellationToken.None);

        result.BookingStatus.ShouldBe(nameof(BookingStatus.Completed));
        context.Set<Booking>().Single().BookingStatus.ShouldBe(BookingStatus.Completed);
    }

    [Test]
    public async Task ActiveTicketCannotBeCheckedOutBeforeCheckIn()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var staffContext = await SeatFlowTestData.SeedStaffAsync(context);
        var ticket = await SeedRegularBookingTicketAsync(context);
        var handler = new CheckOutTicketCommandHandler(
            context,
            staffContext,
            new FixedTimeProvider(new DateTimeOffset(2030, 1, 1, 10, 0, 0, TimeSpan.Zero)));

        var ex = await Should.ThrowAsync<ValidationException>(() =>
            handler.Handle(new CheckOutTicketCommand(ticket.TicketCode), CancellationToken.None));

        ex.Errors["ticket"].Single().ShouldBe("Ve chua check-in nen chua the check-out.");
    }

    [Test]
    public async Task CheckedOutTicketCannotBeCheckedOutAgain()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var staffContext = await SeatFlowTestData.SeedStaffAsync(context);
        var ticket = await SeedRegularBookingTicketAsync(
            context,
            TicketStatus.CheckedOut,
            new DateTimeOffset(2030, 1, 1, 9, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2030, 1, 1, 10, 0, 0, TimeSpan.Zero));
        var handler = new CheckOutTicketCommandHandler(
            context,
            staffContext,
            new FixedTimeProvider(new DateTimeOffset(2030, 1, 1, 10, 5, 0, TimeSpan.Zero)));

        var ex = await Should.ThrowAsync<ValidationException>(() =>
            handler.Handle(new CheckOutTicketCommand(ticket.TicketCode), CancellationToken.None));

        ex.Errors["ticket"].Single().ShouldBe("Ve nay da duoc check-out.");
    }

    [Test]
    public async Task StaffCanReissueActiveTicketWithReason()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var staffContext = await SeatFlowTestData.SeedStaffAsync(context);
        var now = new DateTimeOffset(2030, 1, 1, 11, 0, 0, TimeSpan.Zero);
        var oldTicket = await SeedRegularBookingTicketAsync(context);
        var handler = new ReissueTicketCommandHandler(
            context,
            staffContext,
            new FixedTimeProvider(now));

        var result = await handler.Handle(
            new ReissueTicketCommand(oldTicket.QrToken, " QR bi loi, khach co booking hop le "),
            CancellationToken.None);

        result.TicketStatus.ShouldBe(nameof(TicketStatus.Active));
        result.TicketId.ShouldNotBe(oldTicket.Id);
        result.TicketCode.ShouldNotBe(oldTicket.TicketCode);
        result.QrToken.ShouldNotBe(oldTicket.QrToken);
        result.TicketPassenger.ShouldNotBeNull();
        result.TicketPassenger.PassengerId.ShouldBe(oldTicket.BookingPassengerId!.Value);

        var tickets = context.Tickets.OrderBy(x => x.IssuedAt).ToArray();
        tickets.Length.ShouldBe(2);
        tickets[0].TicketStatus.ShouldBe(TicketStatus.Cancelled);
        tickets[1].TicketStatus.ShouldBe(TicketStatus.Active);
        tickets[1].ReissuedFromTicketId.ShouldBe(oldTicket.Id);
        tickets[1].ReissueReason.ShouldBe("QR bi loi, khach co booking hop le");
        tickets[1].ReissuedAt.ShouldBe(now);
        tickets[1].ReissuedByUserId.ShouldBe(staffContext.UserId!.Value);
    }

    [Test]
    public async Task ManagerCanReissueActiveTicketWithReason()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var managerContext = await SeatFlowTestData.SeedManagerAsync(context);
        var ticket = await SeedRegularBookingTicketAsync(context);
        var handler = new ReissueTicketCommandHandler(
            context,
            managerContext,
            new FixedTimeProvider(new DateTimeOffset(2030, 1, 1, 11, 0, 0, TimeSpan.Zero)));

        var result = await handler.Handle(
            new ReissueTicketCommand(ticket.TicketCode, "Khach doi ve vi QR mo"),
            CancellationToken.None);

        result.TicketStatus.ShouldBe(nameof(TicketStatus.Active));
        context.Tickets.Count().ShouldBe(2);
    }

    [Test]
    public async Task CheckedInTicketCannotBeReissued()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var staffContext = await SeatFlowTestData.SeedStaffAsync(context);
        var ticket = await SeedRegularBookingTicketAsync(
            context,
            TicketStatus.CheckedIn,
            new DateTimeOffset(2030, 1, 1, 9, 0, 0, TimeSpan.Zero));
        var handler = new ReissueTicketCommandHandler(
            context,
            staffContext,
            new FixedTimeProvider(new DateTimeOffset(2030, 1, 1, 11, 0, 0, TimeSpan.Zero)));

        var ex = await Should.ThrowAsync<ValidationException>(() =>
            handler.Handle(new ReissueTicketCommand(ticket.TicketCode, "QR loi"), CancellationToken.None));

        ex.Errors["ticket"].Single().ShouldBe("Chi co the cap lai ve dang Active.");
    }

    [Test]
    public void ReissueReasonIsRequired()
    {
        var validator = new ReissueTicketCommandValidator();

        var result = validator.Validate(new ReissueTicketCommand("TK0001", ""));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(x => x.PropertyName == nameof(ReissueTicketCommand.Reason));
    }

    private static async Task<Ticket> SeedRegularBookingTicketAsync(
        DbContext context,
        TicketStatus ticketStatus = TicketStatus.Active,
        DateTimeOffset? checkedInAt = null,
        DateTimeOffset? checkedOutAt = null)
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
            IsActive = true
        };
        var route = new Route
        {
            RouteCode = $"RT-{Guid.NewGuid():N}"[..20],
            RouteName = "Test Route"
        };
        var trip = new Trip
        {
            Route = route,
            Boat = boat,
            TripCode = $"TR-{Guid.NewGuid():N}"[..20],
            OperatingDate = new DateOnly(2030, 1, 1),
            DepartureTime = new DateTimeOffset(2030, 1, 1, 8, 0, 0, TimeSpan.Zero),
            ArrivalTime = new DateTimeOffset(2030, 1, 1, 9, 0, 0, TimeSpan.Zero),
            CapacitySnapshot = 1
        };
        var tripSeat = new TripSeat
        {
            Trip = trip,
            Seat = seat,
            Status = TripSeat.StatusBooked
        };
        var booking = new Booking
        {
            Trip = trip,
            BookingCode = $"BK-{Guid.NewGuid():N}"[..20],
            ContactName = "Nguyen Van A",
            ContactPhone = "0900000000",
            ContactEmail = "customer@example.test",
            BookingStatus = BookingStatus.Confirmed,
            PaymentStatus = "Paid",
            SubtotalAmount = 10000,
            TotalAmount = 10000,
            DepositAmount = 10000,
            RemainingAmount = 0
        };
        var passenger = new BookingPassenger
        {
            Booking = booking,
            FullName = "Nguyen Van A",
            PhoneNumber = "0900000001",
            PassengerType = "ADULT",
            TripSeat = tripSeat,
            UnitPrice = 10000
        };
        var ticket = new Ticket
        {
            Booking = booking,
            BookingPassenger = passenger,
            BookingPassengerId = passenger.Id,
            TicketCode = $"TK{Guid.NewGuid():N}"[..20],
            QrToken = Convert.ToHexString(Guid.NewGuid().ToByteArray()),
            TicketStatus = ticketStatus,
            IssuedAt = new DateTimeOffset(2030, 1, 1, 8, 0, 0, TimeSpan.Zero),
            CheckedInAt = checkedInAt,
            CheckedOutAt = checkedOutAt
        };

        context.AddRange(booking, passenger, ticket);
        await context.SaveChangesAsync();
        return ticket;
    }

    private static async Task AddOnBoardAssignmentAsync(
        DbContext context,
        Guid staffUserId,
        Ticket ticket,
        DateTimeOffset startAt,
        DateTimeOffset endAt)
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
            StartAt = startAt,
            EndAt = endAt,
            Status = StaffWorkAssignmentStatus.Scheduled,
            AssignedByUserId = staffUserId,
            AssignedAt = startAt.AddHours(-1)
        });
        await context.SaveChangesAsync();
    }

    private static async Task<Ticket> AddTicketToBookingAsync(
        DbContext context,
        Booking booking,
        string passengerName,
        TicketStatus ticketStatus,
        DateTimeOffset? checkedInAt = null,
        DateTimeOffset? checkedOutAt = null)
    {
        var passenger = new BookingPassenger
        {
            Booking = booking,
            FullName = passengerName,
            PhoneNumber = "0900000002",
            PassengerType = "ADULT"
        };
        var ticket = new Ticket
        {
            Booking = booking,
            BookingPassenger = passenger,
            TicketCode = $"TK{Guid.NewGuid():N}"[..20],
            QrToken = Convert.ToHexString(Guid.NewGuid().ToByteArray()),
            TicketStatus = ticketStatus,
            IssuedAt = new DateTimeOffset(2030, 1, 1, 8, 0, 0, TimeSpan.Zero),
            CheckedInAt = checkedInAt,
            CheckedOutAt = checkedOutAt
        };

        context.AddRange(passenger, ticket);
        await context.SaveChangesAsync();
        return ticket;
    }

    private static async Task<User> SeedCustomerAsync(DbContext context)
    {
        var role = new Role
        {
            Code = Roles.CustomerCode,
            SystemName = Roles.CustomerSystemName,
            DisplayName = "Customer"
        };
        var user = new User
        {
            FullName = "Customer",
            Role = role,
            RoleId = role.Id,
            Status = UserStatus.Active
        };

        context.AddRange(role, user);
        await context.SaveChangesAsync();
        return user;
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
