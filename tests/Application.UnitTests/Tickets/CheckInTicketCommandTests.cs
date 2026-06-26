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
        var handler = new CheckInTicketCommandHandler(
            context,
            staffContext,
            new FixedTimeProvider(now));

        var result = await handler.Handle(new CheckInTicketCommand(ticket.QrToken), CancellationToken.None);

        result.TicketStatus.ShouldBe(nameof(BookingTicketStatus.CheckedIn));
        result.CheckedInAt.ShouldBe(now);
        result.CheckedInByUserId.ShouldBe(staffContext.UserId!.Value);
        result.TicketPassenger.ShouldNotBeNull();
        result.TicketPassenger.FullName.ShouldBe("Nguyen Van A");
        result.SeatCode.ShouldBe("A1");

        var savedTicket = context.Tickets.Single();
        savedTicket.TicketStatus.ShouldBe(BookingTicketStatus.CheckedIn);
        savedTicket.CheckedInAt.ShouldBe(now);
        savedTicket.CheckedInByUserId.ShouldBe(staffContext.UserId!.Value);
    }

    [Test]
    public async Task CheckedInTicketCannotBeCheckedInAgain()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var staffContext = await SeatFlowTestData.SeedStaffAsync(context);
        var ticket = await SeedRegularBookingTicketAsync(
            context,
            BookingTicketStatus.CheckedIn,
            new DateTimeOffset(2030, 1, 1, 9, 0, 0, TimeSpan.Zero));
        var handler = new CheckInTicketCommandHandler(
            context,
            staffContext,
            new FixedTimeProvider(new DateTimeOffset(2030, 1, 1, 9, 5, 0, TimeSpan.Zero)));

        var ex = await Should.ThrowAsync<ValidationException>(() =>
            handler.Handle(new CheckInTicketCommand(ticket.TicketCode), CancellationToken.None));

        ex.Errors["ticket"].Single().ShouldBe("Ve nay da duoc check-in.");
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
            BookingTicketStatus.CheckedIn,
            checkedInAt);
        var handler = new CheckOutTicketCommandHandler(
            context,
            staffContext,
            new FixedTimeProvider(checkedOutAt));

        var result = await handler.Handle(new CheckOutTicketCommand(ticket.QrToken), CancellationToken.None);

        result.TicketStatus.ShouldBe(nameof(BookingTicketStatus.CheckedOut));
        result.CheckedInAt.ShouldBe(checkedInAt);
        result.CheckedOutAt.ShouldBe(checkedOutAt);
        result.CheckedOutByUserId.ShouldBe(staffContext.UserId!.Value);

        var savedTicket = context.Tickets.Single();
        savedTicket.TicketStatus.ShouldBe(BookingTicketStatus.CheckedOut);
        savedTicket.CheckedOutAt.ShouldBe(checkedOutAt);
        savedTicket.CheckedOutByUserId.ShouldBe(staffContext.UserId!.Value);
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
            BookingTicketStatus.CheckedOut,
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

    private static async Task<BookingTicket> SeedRegularBookingTicketAsync(
        DbContext context,
        BookingTicketStatus ticketStatus = BookingTicketStatus.Active,
        DateTimeOffset? checkedInAt = null,
        DateTimeOffset? checkedOutAt = null)
    {
        var booking = new Booking
        {
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
            SeatCode = "A1",
            UnitPrice = 10000
        };
        var ticket = new BookingTicket
        {
            Booking = booking,
            BookingPassenger = passenger,
            TicketCode = $"TK{Guid.NewGuid():N}"[..20],
            QrToken = Convert.ToHexString(Guid.NewGuid().ToByteArray()),
            TicketTypeCode = "ADULT",
            TicketTypeName = "Ve nguoi lon",
            TicketStatus = ticketStatus,
            IssuedAt = new DateTimeOffset(2030, 1, 1, 8, 0, 0, TimeSpan.Zero),
            CheckedInAt = checkedInAt,
            CheckedOutAt = checkedOutAt
        };

        context.AddRange(booking, passenger, ticket);
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
