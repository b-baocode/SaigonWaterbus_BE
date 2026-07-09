using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using SaigonWaterbus.Application.Common.Exceptions;
using SaigonWaterbus.Application.StationStaffAssignments;
using SaigonWaterbus.Application.UnitTests.TestInfrastructure;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using Shouldly;

namespace SaigonWaterbus.Application.UnitTests.StationStaffAssignments;

public class StationStaffAssignmentTests
{
    [Test]
    public async Task StationManagerCanAssignGroundStaffToOwnedStationForCharterBooking()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var manager = await SeatFlowTestData.SeedManagerAsync(context);
        var staff = await SeatFlowTestData.SeedStaffAsync(context, StaffType.Ground);
        var station = Station();
        var booking = CharterBooking(station.Id);
        context.AddRange(station, booking);
        await context.SaveChangesAsync();
        await AddStationAssignmentAsync(context, manager.UserId!.Value, station.Id);
        await AddStationAssignmentAsync(context, staff.UserId!.Value, station.Id);

        var assignedAt = new DateTimeOffset(2030, 1, 1, 9, 0, 0, TimeSpan.Zero);
        var handler = new AssignStationStaffCommandHandler(
            context,
            manager,
            new FixedTimeProvider(assignedAt));

        var result = await handler.Handle(
            new AssignStationStaffCommand(
                station.Id,
                staff.UserId.Value,
                OperationScheduleSourceType.CharterBooking,
                booking.Id,
                booking.DepartureDate!.Value,
                null,
                "CheckIn"),
            CancellationToken.None);

        result.StationId.ShouldBe(station.Id);
        result.StaffUserId.ShouldBe(staff.UserId.Value);
        result.ShiftCode.ShouldBe("Day");
        result.DutyRole.ShouldBe("CheckIn");
        result.AssignedByUserId.ShouldBe(manager.UserId.Value);
        result.AssignedAt.ShouldBe(assignedAt);

        context.StationStaffAssignments.Single().IsActive.ShouldBeTrue();
    }

    [Test]
    public async Task StationManagerCannotAssignOnBoardStaffToStation()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var manager = await SeatFlowTestData.SeedManagerAsync(context);
        var staff = await SeatFlowTestData.SeedStaffAsync(context, StaffType.OnBoard);
        var station = Station();
        var booking = CharterBooking(station.Id);
        context.AddRange(station, booking);
        await context.SaveChangesAsync();
        await AddStationAssignmentAsync(context, manager.UserId!.Value, station.Id);

        var handler = new AssignStationStaffCommandHandler(
            context,
            manager,
            TimeProvider.System);

        var exception = await Should.ThrowAsync<ValidationException>(() =>
            handler.Handle(
                new AssignStationStaffCommand(
                    station.Id,
                    staff.UserId!.Value,
                    OperationScheduleSourceType.CharterBooking,
                    booking.Id,
                    booking.DepartureDate!.Value),
                CancellationToken.None));

        exception.Errors["staffUserId"].Single()
            .ShouldBe("Chỉ nhân viên mặt đất mới được phân công tại bến.");
    }

    private static Station Station() =>
        new()
        {
            StationCode = $"ST{Guid.NewGuid():N}"[..10],
            StationName = "Bach Dang",
            Status = StationStatus.Active
        };

    private static Booking CharterBooking(Guid stationId) =>
        new()
        {
            BookingType = Booking.CharterBookingType,
            BookingCode = $"CB{Guid.NewGuid():N}"[..12].ToUpperInvariant(),
            ContactName = "Nguyen Van A",
            ContactPhone = "0900000000",
            DepartureDate = new DateOnly(2030, 1, 2),
            FromStationId = stationId,
            AdultCount = 10,
            ChildCount = 0,
            PassengerCount = 10,
            RequestedBoatCount = 1,
            BookingStatus = BookingStatus.Quoted,
            PaymentStatus = "Unpaid"
        };

    private static async Task AddStationAssignmentAsync(
        DbContext context,
        Guid userId,
        Guid stationId)
    {
        context.Set<UserStationAssignment>().Add(new UserStationAssignment
        {
            UserId = userId,
            StationId = stationId,
            IsPrimary = true,
            IsActive = true,
            AssignedAt = new DateTimeOffset(2030, 1, 1, 8, 0, 0, TimeSpan.Zero),
            AssignedByUserId = userId
        });
        await context.SaveChangesAsync();
    }
}
