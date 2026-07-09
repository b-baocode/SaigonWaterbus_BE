using NUnit.Framework;
using SaigonWaterbus.Application.BoatCrewAssignments;
using SaigonWaterbus.Application.Common.Exceptions;
using SaigonWaterbus.Application.UnitTests.TestInfrastructure;
using SaigonWaterbus.Domain.Enums;
using Shouldly;

namespace SaigonWaterbus.Application.UnitTests.BoatCrewAssignments;

public class BoatCrewAssignmentTests
{
    [Test]
    public async Task ManagerCanCreateBoatCrewAssignmentForDateRange()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var managerContext = await SeatFlowTestData.SeedManagerAsync(context);
        var staffContext = await SeatFlowTestData.SeedStaffAsync(context);
        var boat = Boat("WB-01");
        context.Boats.Add(boat);
        await context.SaveChangesAsync();

        var assignedAt = new DateTimeOffset(2030, 1, 1, 1, 0, 0, TimeSpan.Zero);
        var handler = new CreateBoatCrewAssignmentCommandHandler(
            context,
            managerContext,
            new FixedTimeProvider(assignedAt));

        var result = await handler.Handle(
            new CreateBoatCrewAssignmentCommand(
                boat.Id,
                staffContext.UserId!.Value,
                new DateOnly(2030, 1, 2),
                new DateOnly(2030, 1, 31)),
            CancellationToken.None);

        result.BoatId.ShouldBe(boat.Id);
        result.StaffUserId.ShouldBe(staffContext.UserId.Value);
        result.CrewRole.ShouldBe(CrewRole.OnBoard);
        result.FromDate.ShouldBe(new DateOnly(2030, 1, 2));
        result.ToDate.ShouldBe(new DateOnly(2030, 1, 31));
        result.AssignedAt.ShouldBe(assignedAt);
        result.IsActive.ShouldBeTrue();

        context.BoatCrewAssignments.Single().IsActive.ShouldBeTrue();
    }

    [Test]
    public async Task CanCreateMultipleOnBoardStaffForSameBoat()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var managerContext = await SeatFlowTestData.SeedManagerAsync(context);
        var firstStaffContext = await SeatFlowTestData.SeedStaffAsync(context);
        var secondStaffContext = await SeatFlowTestData.SeedStaffAsync(context);
        var boat = Boat("WB-01");
        context.Boats.Add(boat);
        await context.SaveChangesAsync();

        var handler = new CreateBoatCrewAssignmentCommandHandler(
            context,
            managerContext,
            TimeProvider.System);

        await handler.Handle(
            new CreateBoatCrewAssignmentCommand(
                boat.Id,
                firstStaffContext.UserId!.Value,
                new DateOnly(2030, 1, 1),
                new DateOnly(2030, 1, 31)),
            CancellationToken.None);

        var second = await handler.Handle(
            new CreateBoatCrewAssignmentCommand(
                boat.Id,
                secondStaffContext.UserId!.Value,
                new DateOnly(2030, 1, 15),
                new DateOnly(2030, 2, 15)),
            CancellationToken.None);

        second.CrewRole.ShouldBe(CrewRole.OnBoard);
        context.BoatCrewAssignments.Count(x => x.BoatId == boat.Id && x.IsActive).ShouldBe(2);
    }

    [Test]
    public async Task CannotAssignSameStaffToOverlappingBoatCrew()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var managerContext = await SeatFlowTestData.SeedManagerAsync(context);
        var staffContext = await SeatFlowTestData.SeedStaffAsync(context);
        var firstBoat = Boat("WB-01");
        var secondBoat = Boat("WB-02");
        context.Boats.AddRange(firstBoat, secondBoat);
        await context.SaveChangesAsync();

        var handler = new CreateBoatCrewAssignmentCommandHandler(
            context,
            managerContext,
            TimeProvider.System);

        await handler.Handle(
            new CreateBoatCrewAssignmentCommand(
                firstBoat.Id,
                staffContext.UserId!.Value,
                new DateOnly(2030, 1, 1),
                new DateOnly(2030, 1, 31)),
            CancellationToken.None);

        var exception = await Should.ThrowAsync<ValidationException>(() =>
            handler.Handle(
                new CreateBoatCrewAssignmentCommand(
                    secondBoat.Id,
                    staffContext.UserId.Value,
                    new DateOnly(2030, 1, 15),
                    new DateOnly(2030, 2, 15)),
                CancellationToken.None));

        exception.Errors["staffUserId"].Single()
            .ShouldBe("Staff này đã được gắn lên tàu khác trong khoảng ngày này.");
    }

    [Test]
    public async Task ReplacementOverridesBaseCrewInCalendar()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var managerContext = await SeatFlowTestData.SeedManagerAsync(context);
        var baseStaffContext = await SeatFlowTestData.SeedStaffAsync(context);
        var replacementStaffContext = await SeatFlowTestData.SeedStaffAsync(context);
        var boat = Boat("WB-01");
        context.Boats.Add(boat);
        await context.SaveChangesAsync();

        var assignmentHandler = new CreateBoatCrewAssignmentCommandHandler(
            context,
            managerContext,
            new FixedTimeProvider(new DateTimeOffset(2030, 1, 1, 1, 0, 0, TimeSpan.Zero)));
        await assignmentHandler.Handle(
            new CreateBoatCrewAssignmentCommand(
                boat.Id,
                baseStaffContext.UserId!.Value,
                new DateOnly(2030, 1, 1),
                new DateOnly(2030, 1, 31)),
            CancellationToken.None);

        var replacementHandler = new CreateBoatCrewReplacementCommandHandler(
            context,
            managerContext,
            new FixedTimeProvider(new DateTimeOffset(2030, 1, 2, 1, 0, 0, TimeSpan.Zero)));
        var replacement = await replacementHandler.Handle(
            new CreateBoatCrewReplacementCommand(
                boat.Id,
                baseStaffContext.UserId.Value,
                replacementStaffContext.UserId!.Value,
                new DateOnly(2030, 1, 10),
                new DateOnly(2030, 1, 12),
                "Nghi phep"),
            CancellationToken.None);

        replacement.CrewRole.ShouldBe(CrewRole.OnBoard);

        var calendarHandler = new GetBoatCrewCalendarQueryHandler(context, managerContext);
        var calendar = await calendarHandler.Handle(
            new GetBoatCrewCalendarQuery(
                boat.Id,
                new DateOnly(2030, 1, 9),
                new DateOnly(2030, 1, 13)),
            CancellationToken.None);

        var normalDayCrew = calendar.Single(x => x.WorkingDate == new DateOnly(2030, 1, 9)).Crew.Single();
        normalDayCrew.StaffUserId.ShouldBe(baseStaffContext.UserId.Value);
        normalDayCrew.CrewRole.ShouldBe(CrewRole.OnBoard);
        normalDayCrew.IsReplacement.ShouldBeFalse();

        var replacementDayCrew = calendar.Single(x => x.WorkingDate == new DateOnly(2030, 1, 10)).Crew.Single();
        replacementDayCrew.StaffUserId.ShouldBe(replacementStaffContext.UserId.Value);
        replacementDayCrew.CrewRole.ShouldBe(CrewRole.OnBoard);
        replacementDayCrew.IsReplacement.ShouldBeTrue();
        replacementDayCrew.ReplacedStaffUserId.ShouldBe(baseStaffContext.UserId.Value);
        replacementDayCrew.ReplacementId.ShouldBe(replacement.ReplacementId);
    }

    [Test]
    public async Task DeletingBaseCrewAssignmentDeactivatesRelatedReplacements()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var managerContext = await SeatFlowTestData.SeedManagerAsync(context);
        var baseStaffContext = await SeatFlowTestData.SeedStaffAsync(context);
        var replacementStaffContext = await SeatFlowTestData.SeedStaffAsync(context);
        var boat = Boat("WB-01");
        context.Boats.Add(boat);
        await context.SaveChangesAsync();

        var assignmentHandler = new CreateBoatCrewAssignmentCommandHandler(
            context,
            managerContext,
            TimeProvider.System);
        var assignment = await assignmentHandler.Handle(
            new CreateBoatCrewAssignmentCommand(
                boat.Id,
                baseStaffContext.UserId!.Value,
                new DateOnly(2030, 1, 1),
                new DateOnly(2030, 1, 31)),
            CancellationToken.None);

        var replacementHandler = new CreateBoatCrewReplacementCommandHandler(
            context,
            managerContext,
            TimeProvider.System);
        await replacementHandler.Handle(
            new CreateBoatCrewReplacementCommand(
                boat.Id,
                baseStaffContext.UserId.Value,
                replacementStaffContext.UserId!.Value,
                new DateOnly(2030, 1, 10),
                new DateOnly(2030, 1, 12),
                "Nghi phep"),
            CancellationToken.None);

        var deleteHandler = new DeleteBoatCrewAssignmentCommandHandler(context, managerContext);
        await deleteHandler.Handle(
            new DeleteBoatCrewAssignmentCommand(boat.Id, assignment.AssignmentId),
            CancellationToken.None);

        context.BoatCrewAssignments.Single(x => x.ReplacesAssignmentId == null).IsActive.ShouldBeFalse();
        context.BoatCrewAssignments.Single(x => x.ReplacesAssignmentId == assignment.AssignmentId)
            .IsActive.ShouldBeFalse();
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
}
