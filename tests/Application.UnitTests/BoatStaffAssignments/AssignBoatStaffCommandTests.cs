using NUnit.Framework;
using SaigonWaterbus.Application.BoatStaffAssignments;
using SaigonWaterbus.Application.Common.Exceptions;
using SaigonWaterbus.Application.UnitTests.TestInfrastructure;
using SaigonWaterbus.Domain.Enums;
using Shouldly;

namespace SaigonWaterbus.Application.UnitTests.BoatStaffAssignments;

public class AssignBoatStaffCommandTests
{
    [Test]
    public async Task ManagerCanAssignStaffToBoatForWorkingDate()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var managerContext = await SeatFlowTestData.SeedManagerAsync(context);
        var staffContext = await SeatFlowTestData.SeedStaffAsync(context);
        var boat = Boat("WB-01");
        context.Boats.Add(boat);
        await context.SaveChangesAsync();

        var now = new DateTimeOffset(2030, 1, 1, 1, 0, 0, TimeSpan.Zero);
        var handler = new AssignBoatStaffCommandHandler(
            context,
            managerContext,
            new FixedTimeProvider(now));

        var result = await handler.Handle(
            new AssignBoatStaffCommand(
                boat.Id,
                staffContext.UserId!.Value,
                new DateOnly(2030, 1, 2),
                null,
                "Captain"),
            CancellationToken.None);

        result.BoatId.ShouldBe(boat.Id);
        result.StaffUserId.ShouldBe(staffContext.UserId.Value);
        result.WorkingDate.ShouldBe(new DateOnly(2030, 1, 2));
        result.ShiftCode.ShouldBe(BoatStaffAssignmentSupport.DefaultShiftCode);
        result.DutyRole.ShouldBe("Captain");
        result.IsActive.ShouldBeTrue();
        result.AssignedAt.ShouldBe(now);

        context.BoatStaffAssignments.Single().ShiftCode.ShouldBe(BoatStaffAssignmentSupport.DefaultShiftCode);
    }

    [Test]
    public async Task StaffCannotBeAssignedToTwoBoatsInSameWorkingDateAndShift()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var managerContext = await SeatFlowTestData.SeedManagerAsync(context);
        var staffContext = await SeatFlowTestData.SeedStaffAsync(context);
        var firstBoat = Boat("WB-01");
        var secondBoat = Boat("WB-02");
        context.Boats.AddRange(firstBoat, secondBoat);
        await context.SaveChangesAsync();

        var handler = new AssignBoatStaffCommandHandler(
            context,
            managerContext,
            new FixedTimeProvider(new DateTimeOffset(2030, 1, 1, 1, 0, 0, TimeSpan.Zero)));
        var workingDate = new DateOnly(2030, 1, 2);

        await handler.Handle(
            new AssignBoatStaffCommand(firstBoat.Id, staffContext.UserId!.Value, workingDate, "Day", "Captain"),
            CancellationToken.None);

        var exception = await Should.ThrowAsync<ValidationException>(() =>
            handler.Handle(
                new AssignBoatStaffCommand(secondBoat.Id, staffContext.UserId.Value, workingDate, "Day", "Deckhand"),
                CancellationToken.None));

        exception.Errors["staffUserId"].Single()
            .ShouldBe("Staff này đã được phân công cho tàu khác trong cùng ngày/ca.");
    }

    private static SaigonWaterbus.Domain.Entities.Boat Boat(string code) =>
        new()
        {
            Code = code,
            Name = code,
            Status = BoatStatus.Active,
            SeatCount = 20,
            NumberOfDecks = 1,
            SeatSetupType = SeatSetupType.FullStandard,
            SeatsConfigured = true
        };

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
