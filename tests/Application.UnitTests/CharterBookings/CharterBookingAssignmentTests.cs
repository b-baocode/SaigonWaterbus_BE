using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using SaigonWaterbus.Application.CharterBookings;
using SaigonWaterbus.Application.Common.Exceptions;
using SaigonWaterbus.Application.UnitTests.TestInfrastructure;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using Shouldly;

namespace SaigonWaterbus.Application.UnitTests.CharterBookings;

public class CharterBookingAssignmentTests
{
    [Test]
    public async Task AdminCanAssignManagerToCharterBooking()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var admin = await SeatFlowTestData.SeedAdminAsync(context);
        var manager = await SeatFlowTestData.SeedManagerAsync(context);
        var booking = CharterBooking();
        context.Add(booking);
        await context.SaveChangesAsync();

        var handler = new AssignCharterBookingManagerCommandHandler(context, admin);

        var result = await handler.Handle(
            new AssignCharterBookingManagerCommand(booking.Id, manager.UserId),
            CancellationToken.None);

        result.AssignedManager.ShouldNotBeNull().UserId.ShouldBe(manager.UserId!.Value);
        context.Set<Booking>().Single(x => x.Id == booking.Id).AssignedManagerId.ShouldBe(manager.UserId);
    }

    [Test]
    public async Task AssignedManagerCanAssignStaffToSelectedCharterBoat()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var manager = await SeatFlowTestData.SeedManagerAsync(context);
        var staff = await SeatFlowTestData.SeedStaffAsync(context);
        var boat = ActiveBoat();
        var booking = CharterBooking(boat);
        booking.AssignedManagerId = manager.UserId;
        context.AddRange(boat, booking);
        await context.SaveChangesAsync();

        var handler = new AssignCharterBookingStaffCommandHandler(
            context,
            manager,
            new FixedTimeProvider(new DateTimeOffset(2026, 7, 8, 1, 0, 0, TimeSpan.Zero)));

        var result = await handler.Handle(
            new AssignCharterBookingStaffCommand(
                booking.Id,
                staff.UserId!.Value),
            CancellationToken.None);

        result.BoatId.ShouldBe(boat.Id);
        result.StaffUserId.ShouldBe(staff.UserId.Value);
        result.WorkingDate.ShouldBe(booking.DepartureDate!.Value);
        result.ShiftCode.ShouldBe("Day");
        result.DutyRole.ShouldBe("OnBoard");

        var assignment = context.BoatStaffAssignments.Single();
        assignment.BoatId.ShouldBe(boat.Id);
        assignment.StaffUserId.ShouldBe(staff.UserId.Value);
        assignment.AssignedByUserId.ShouldBe(manager.UserId!.Value);
    }

    [Test]
    public async Task UnassignedManagerCannotAssignStaffToCharterBooking()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var assignedManager = await SeatFlowTestData.SeedManagerAsync(context);
        var otherManager = await SeatFlowTestData.SeedManagerAsync(context);
        var staff = await SeatFlowTestData.SeedStaffAsync(context);
        var boat = ActiveBoat();
        var booking = CharterBooking(boat);
        booking.AssignedManagerId = assignedManager.UserId;
        context.AddRange(boat, booking);
        await context.SaveChangesAsync();

        var handler = new AssignCharterBookingStaffCommandHandler(
            context,
            otherManager,
            TimeProvider.System);

        await Should.ThrowAsync<ForbiddenAccessException>(() =>
            handler.Handle(
                new AssignCharterBookingStaffCommand(booking.Id, staff.UserId!.Value),
                CancellationToken.None));
    }

    [Test]
    public async Task AssignedStaffCanCheckInCharterBookingQr()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var manager = await SeatFlowTestData.SeedManagerAsync(context);
        var staff = await SeatFlowTestData.SeedStaffAsync(context);
        var customer = Customer();
        var boat = ActiveBoat();
        var booking = CharterBooking(boat, customer.Id);
        booking.AssignedManagerId = manager.UserId;
        booking.CharterBookingQrToken = $"CB{Guid.NewGuid():N}"[..30];
        AddPassengerTicket(booking);
        context.AddRange(customer.Role, customer, boat, booking);
        await context.SaveChangesAsync();

        var assignHandler = new AssignCharterBookingStaffCommandHandler(
            context,
            manager,
            TimeProvider.System);
        await assignHandler.Handle(
            new AssignCharterBookingStaffCommand(booking.Id, staff.UserId!.Value),
            CancellationToken.None);

        var checkedInAt = new DateTimeOffset(2030, 1, 1, 9, 0, 0, TimeSpan.Zero);
        var attendanceHandler = new UpdateCharterBookingAttendanceCommandHandler(
            context,
            staff,
            new FixedTimeProvider(checkedInAt));

        var result = await attendanceHandler.Handle(
            new UpdateCharterBookingAttendanceCommand(
                booking.CharterBookingQrToken,
                CharterBookingAttendanceAction.CheckIn,
                CharterBookingAttendanceMode.All,
                null),
            CancellationToken.None);

        result.UpdatedCount.ShouldBe(1);
        context.Tickets.Single().CheckedInByUserId.ShouldBe(staff.UserId);
    }

    [Test]
    public async Task UnassignedStaffCannotCheckInCharterBookingQr()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var staff = await SeatFlowTestData.SeedStaffAsync(context);
        var customer = Customer();
        var boat = ActiveBoat();
        var booking = CharterBooking(boat, customer.Id);
        booking.CharterBookingQrToken = $"CB{Guid.NewGuid():N}"[..30];
        AddPassengerTicket(booking);
        context.AddRange(customer.Role, customer, boat, booking);
        await context.SaveChangesAsync();

        var attendanceHandler = new UpdateCharterBookingAttendanceCommandHandler(
            context,
            staff,
            TimeProvider.System);

        await Should.ThrowAsync<ForbiddenAccessException>(() =>
            attendanceHandler.Handle(
                new UpdateCharterBookingAttendanceCommand(
                    booking.CharterBookingQrToken,
                    CharterBookingAttendanceAction.CheckIn,
                    CharterBookingAttendanceMode.All,
                    null),
                CancellationToken.None));
    }

    private static Boat ActiveBoat()
    {
        var boat = SeatFlowTestData.Boat(SeatSetupType.FullStandard, seatsConfigured: true, status: BoatStatus.Active);
        boat.Name = "Charter Boat";
        boat.SeatCount = 50;
        boat.DailyRentalPrice = 1_000_000m;
        return boat;
    }

    private static Booking CharterBooking(Boat? boat = null, Guid? customerId = null) =>
        new()
        {
            BookingType = Booking.CharterBookingType,
            UserId = customerId,
            BoatId = boat?.Id,
            Boat = boat,
            BookingCode = $"CB{Guid.NewGuid():N}"[..12].ToUpperInvariant(),
            ContactName = "Nguyen Van A",
            ContactPhone = "0900000000",
            BookingStatus = BookingStatus.Confirmed,
            PaymentStatus = "Paid",
            DepartureDate = new DateOnly(2030, 1, 1),
            RentalUnit = BoatRentalUnit.Day,
            DurationValue = 1,
            AdultCount = 1,
            ChildCount = 0,
            PassengerCount = 1,
            SubtotalAmount = 1_000_000m,
            TotalAmount = 1_000_000m,
            DepositAmount = 1_000_000m,
            RemainingAmount = 0m
        };

    private static void AddPassengerTicket(Booking booking)
    {
        var passenger = new BookingPassenger
        {
            Booking = booking,
            BookingId = booking.Id,
            FullName = "Nguyen Van A",
            PassengerType = CharterBookingPassengerType.Adult.ToString()
        };

        var ticket = new Ticket
        {
            Booking = booking,
            BookingId = booking.Id,
            BookingPassenger = passenger,
            BookingPassengerId = passenger.Id,
            TicketCode = $"TK{Guid.NewGuid():N}"[..20],
            QrToken = $"QR{Guid.NewGuid():N}"[..20],
            TicketStatus = TicketStatus.Active,
            IssuedAt = DateTimeOffset.UtcNow
        };

        booking.Passengers.Add(passenger);
        booking.Tickets.Add(ticket);
    }

    private static User Customer()
    {
        var role = new Role
        {
            Code = Roles.CustomerCode,
            SystemName = Roles.CustomerSystemName,
            DisplayName = "Customer"
        };

        return new User
        {
            FullName = "Customer",
            PhoneNumber = "0900000000",
            RoleId = role.Id,
            Role = role,
            Status = UserStatus.Active
        };
    }
}
