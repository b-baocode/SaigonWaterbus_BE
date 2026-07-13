using NUnit.Framework;
using SaigonWaterbus.Application.Common.Exceptions;
using SaigonWaterbus.Application.StaffWorkAssignments;
using SaigonWaterbus.Application.UnitTests.TestInfrastructure;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using Shouldly;

namespace SaigonWaterbus.Application.UnitTests.StaffWorkAssignments;

public class StaffWorkAssignmentTests
{
    [Test]
    public async Task AdminCanCreateBoatAssignmentForOnBoardStaff()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var adminContext = await SeatFlowTestData.SeedAdminAsync(context);
        var staffContext = await SeatFlowTestData.SeedStaffAsync(context, StaffType.OnBoard);
        var boat = Boat("WB-01");
        context.Boats.Add(boat);
        await context.SaveChangesAsync();

        var assignedAt = new DateTimeOffset(2030, 1, 1, 1, 0, 0, TimeSpan.Zero);
        var handler = new CreateStaffWorkAssignmentCommandHandler(
            context,
            adminContext,
            new FixedTimeProvider(assignedAt));

        var startAt = new DateTimeOffset(2030, 1, 2, 8, 0, 0, TimeSpan.FromHours(7));
        var endAt = new DateTimeOffset(2030, 1, 2, 16, 0, 0, TimeSpan.FromHours(7));
        var result = await handler.Handle(
            new CreateStaffWorkAssignmentCommand(
                staffContext.UserId!.Value,
                StaffWorkAssignmentType.Boat,
                BoatId: boat.Id,
                StartAt: startAt,
                EndAt: endAt,
                DutyRole: "OnBoard"),
            CancellationToken.None);

        result.AssignmentType.ShouldBe(StaffWorkAssignmentType.Boat);
        result.Boat.ShouldNotBeNull().BoatId.ShouldBe(boat.Id);
        result.StaffUserId.ShouldBe(staffContext.UserId.Value);
        result.WorkingDate.ShouldBe(new DateOnly(2030, 1, 2));
        result.AssignedAt.ShouldBe(assignedAt);
        result.Status.ShouldBe(StaffWorkAssignmentStatus.Scheduled);

        context.StaffWorkAssignments.Single().Status.ShouldBe(StaffWorkAssignmentStatus.Scheduled);
    }

    [Test]
    public async Task ManagerCannotCreateBoatAssignment()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var managerContext = await SeatFlowTestData.SeedManagerAsync(context);
        var staffContext = await SeatFlowTestData.SeedStaffAsync(context, StaffType.OnBoard);
        var boat = Boat("WB-01");
        context.Boats.Add(boat);
        await context.SaveChangesAsync();

        var handler = new CreateStaffWorkAssignmentCommandHandler(
            context,
            managerContext,
            TimeProvider.System);

        await Should.ThrowAsync<ForbiddenAccessException>(() =>
            handler.Handle(
                new CreateStaffWorkAssignmentCommand(
                    staffContext.UserId!.Value,
                    StaffWorkAssignmentType.Boat,
                    BoatId: boat.Id,
                    StartAt: new DateTimeOffset(2030, 1, 2, 8, 0, 0, TimeSpan.FromHours(7)),
                    EndAt: new DateTimeOffset(2030, 1, 2, 16, 0, 0, TimeSpan.FromHours(7))),
                CancellationToken.None));
    }

    [Test]
    public async Task CannotAssignSameStaffToOverlappingShift()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var adminContext = await SeatFlowTestData.SeedAdminAsync(context);
        var staffContext = await SeatFlowTestData.SeedStaffAsync(context, StaffType.OnBoard);
        var firstBoat = Boat("WB-01");
        var secondBoat = Boat("WB-02");
        context.Boats.AddRange(firstBoat, secondBoat);
        await context.SaveChangesAsync();

        var handler = new CreateStaffWorkAssignmentCommandHandler(
            context,
            adminContext,
            TimeProvider.System);

        await handler.Handle(
            new CreateStaffWorkAssignmentCommand(
                staffContext.UserId!.Value,
                StaffWorkAssignmentType.Boat,
                BoatId: firstBoat.Id,
                StartAt: new DateTimeOffset(2030, 1, 2, 8, 0, 0, TimeSpan.FromHours(7)),
                EndAt: new DateTimeOffset(2030, 1, 2, 16, 0, 0, TimeSpan.FromHours(7))),
            CancellationToken.None);

        var exception = await Should.ThrowAsync<ValidationException>(() =>
            handler.Handle(
                new CreateStaffWorkAssignmentCommand(
                    staffContext.UserId.Value,
                    StaffWorkAssignmentType.Boat,
                    BoatId: secondBoat.Id,
                    StartAt: new DateTimeOffset(2030, 1, 2, 15, 0, 0, TimeSpan.FromHours(7)),
                    EndAt: new DateTimeOffset(2030, 1, 2, 20, 0, 0, TimeSpan.FromHours(7))),
                CancellationToken.None));

        exception.Errors["staffUserId"].Single()
            .ShouldBe("Staff này đã có ca làm trùng thời gian.");
    }

    [Test]
    public async Task ManagerCanCreateStationAssignmentWithinManagedStation()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var managerContext = await SeatFlowTestData.SeedManagerAsync(context);
        var staffContext = await SeatFlowTestData.SeedStaffAsync(context, StaffType.Ground);
        var station = Station("BD");
        var managerUser = context.Users.Single(x => x.Id == managerContext.UserId!.Value);
        var staffUser = context.Users.Single(x => x.Id == staffContext.UserId!.Value);
        context.Add(station);
        context.Set<UserStationAssignment>().AddRange(
            StationAssignment(managerUser, station, managerUser.Id),
            StationAssignment(staffUser, station, managerUser.Id));
        await context.SaveChangesAsync();

        var handler = new CreateStaffWorkAssignmentCommandHandler(
            context,
            managerContext,
            TimeProvider.System);

        var result = await handler.Handle(
            new CreateStaffWorkAssignmentCommand(
                staffContext.UserId!.Value,
                StaffWorkAssignmentType.Station,
                StationId: station.Id,
                StartAt: new DateTimeOffset(2030, 1, 2, 8, 0, 0, TimeSpan.FromHours(7)),
                EndAt: new DateTimeOffset(2030, 1, 2, 16, 0, 0, TimeSpan.FromHours(7))),
            CancellationToken.None);

        result.AssignmentType.ShouldBe(StaffWorkAssignmentType.Station);
        result.Station.ShouldNotBeNull().StationId.ShouldBe(station.Id);
    }

    [Test]
    public async Task StaffCanSeeCurrentShift()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var adminContext = await SeatFlowTestData.SeedAdminAsync(context);
        var staffContext = await SeatFlowTestData.SeedStaffAsync(context, StaffType.OnBoard);
        var boat = Boat("WB-01");
        context.Boats.Add(boat);
        await context.SaveChangesAsync();

        var now = new DateTimeOffset(2030, 1, 2, 3, 0, 0, TimeSpan.Zero);
        var createHandler = new CreateStaffWorkAssignmentCommandHandler(
            context,
            adminContext,
            new FixedTimeProvider(now.AddHours(-1)));
        await createHandler.Handle(
            new CreateStaffWorkAssignmentCommand(
                staffContext.UserId!.Value,
                StaffWorkAssignmentType.Boat,
                BoatId: boat.Id,
                StartAt: now.AddHours(-1),
                EndAt: now.AddHours(3)),
            CancellationToken.None);

        var currentHandler = new GetMyCurrentStaffShiftQueryHandler(
            context,
            staffContext,
            new FixedTimeProvider(now));
        var result = await currentHandler.Handle(new GetMyCurrentStaffShiftQuery(), CancellationToken.None);

        result.CurrentShift.ShouldNotBeNull().ShiftState.ShouldBe("Active");
        result.TodayAssignments.Count.ShouldBe(1);
    }

    private static Boat Boat(string code) =>
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

    private static Station Station(string code) =>
        new()
        {
            StationCode = code,
            StationName = code,
            Status = StationStatus.Active
        };

    private static UserStationAssignment StationAssignment(User user, Station station, Guid assignedByUserId) =>
        new()
        {
            UserId = user.Id,
            User = user,
            StationId = station.Id,
            Station = station,
            IsActive = true,
            IsPrimary = true,
            AssignedByUserId = assignedByUserId,
            AssignedAt = DateTimeOffset.UtcNow
        };
}
