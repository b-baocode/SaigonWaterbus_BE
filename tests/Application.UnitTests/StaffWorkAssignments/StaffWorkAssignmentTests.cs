using NUnit.Framework;
using SaigonWaterbus.Application.Common.Exceptions;
using SaigonWaterbus.Application.StaffWorkAssignments;
using SaigonWaterbus.Application.Trips;
using SaigonWaterbus.Application.UnitTests.TestInfrastructure;
using SaigonWaterbus.Domain.Constants;
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
    public async Task AdminCanCreateBulkBoatAssignmentsForSelectedWeekdays()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var adminContext = await SeatFlowTestData.SeedAdminAsync(context);
        var staffContext = await SeatFlowTestData.SeedStaffAsync(context, StaffType.OnBoard);
        var boat = Boat("WB-01");
        context.Boats.Add(boat);
        await context.SaveChangesAsync();

        var handler = new CreateBulkStaffWorkAssignmentsCommandHandler(
            context,
            adminContext,
            new FixedTimeProvider(new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero)));

        var result = await handler.Handle(
            new CreateBulkStaffWorkAssignmentsCommand(
                staffContext.UserId!.Value,
                StaffWorkAssignmentType.Boat,
                BoatId: boat.Id,
                StationId: null,
                FromDate: new DateOnly(2030, 1, 7),
                ToDate: new DateOnly(2030, 1, 13),
                StartTime: new TimeOnly(7, 30),
                EndTime: new TimeOnly(15, 0),
                DaysOfWeek: new[] { 1, 3, 5 },
                DutyRole: "OnBoard"),
            CancellationToken.None);

        result.Count.ShouldBe(3);
        result.Select(x => x.WorkingDate).ShouldBe([
            new DateOnly(2030, 1, 7),
            new DateOnly(2030, 1, 9),
            new DateOnly(2030, 1, 11)
        ]);
        result.ShouldAllBe(x => x.Boat != null && x.Boat.BoatId == boat.Id);
        result.ShouldAllBe(x => x.StartAt.ToOffset(TimeSpan.FromHours(7)).TimeOfDay == new TimeSpan(7, 30, 0));
        result.ShouldAllBe(x => x.EndAt.ToOffset(TimeSpan.FromHours(7)).TimeOfDay == new TimeSpan(15, 0, 0));
        context.StaffWorkAssignments.Count().ShouldBe(3);
    }

    [Test]
    public async Task ManagerCanCreateBulkStationAssignmentsWithinManagedStation()
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

        var handler = new CreateBulkStaffWorkAssignmentsCommandHandler(
            context,
            managerContext,
            new FixedTimeProvider(new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero)));

        var result = await handler.Handle(
            new CreateBulkStaffWorkAssignmentsCommand(
                staffContext.UserId!.Value,
                StaffWorkAssignmentType.Station,
                BoatId: null,
                StationId: station.Id,
                FromDate: new DateOnly(2030, 1, 7),
                ToDate: new DateOnly(2030, 1, 9),
                StartTime: new TimeOnly(15, 30),
                EndTime: new TimeOnly(23, 0),
                DaysOfWeek: null,
                DutyRole: "Gate"),
            CancellationToken.None);

        result.Count.ShouldBe(3);
        result.ShouldAllBe(x => x.AssignmentType == StaffWorkAssignmentType.Station);
        result.ShouldAllBe(x => x.Station != null && x.Station.StationId == station.Id);
    }

    [Test]
    public async Task SingleAssignmentCannotSpanMultipleDays()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var adminContext = await SeatFlowTestData.SeedAdminAsync(context);
        var staffContext = await SeatFlowTestData.SeedStaffAsync(context, StaffType.OnBoard);
        var boat = Boat("WB-01");
        context.Boats.Add(boat);
        await context.SaveChangesAsync();

        var handler = new CreateStaffWorkAssignmentCommandHandler(
            context,
            adminContext,
            TimeProvider.System);

        var exception = await Should.ThrowAsync<ValidationException>(() =>
            handler.Handle(
                new CreateStaffWorkAssignmentCommand(
                    staffContext.UserId!.Value,
                    StaffWorkAssignmentType.Boat,
                    BoatId: boat.Id,
                    StartAt: new DateTimeOffset(2030, 1, 2, 8, 0, 0, TimeSpan.FromHours(7)),
                    EndAt: new DateTimeOffset(2030, 1, 4, 16, 0, 0, TimeSpan.FromHours(7))),
                CancellationToken.None));

        exception.Errors["endAt"].Single().ShouldContain("bulk/recurring");
    }

    [Test]
    public async Task AdminCanReplaceBoatAssignmentWithAnotherOnBoardStaff()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var adminContext = await SeatFlowTestData.SeedAdminAsync(context);
        var originalStaffContext = await SeatFlowTestData.SeedStaffAsync(context, StaffType.OnBoard);
        var replacementStaffContext = await SeatFlowTestData.SeedStaffAsync(context, StaffType.OnBoard);
        var boat = Boat("WB-01");
        context.Boats.Add(boat);
        await context.SaveChangesAsync();

        var createHandler = new CreateStaffWorkAssignmentCommandHandler(
            context,
            adminContext,
            new FixedTimeProvider(new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero)));
        var created = await createHandler.Handle(
            new CreateStaffWorkAssignmentCommand(
                originalStaffContext.UserId!.Value,
                StaffWorkAssignmentType.Boat,
                BoatId: boat.Id,
                StartAt: new DateTimeOffset(2030, 1, 3, 7, 30, 0, TimeSpan.FromHours(7)),
                EndAt: new DateTimeOffset(2030, 1, 3, 15, 0, 0, TimeSpan.FromHours(7)),
                DutyRole: "OnBoard"),
            CancellationToken.None);

        var now = new DateTimeOffset(2030, 1, 2, 0, 0, 0, TimeSpan.Zero);
        var replaceHandler = new ReplaceStaffWorkAssignmentCommandHandler(
            context,
            adminContext,
            new FixedTimeProvider(now));

        var result = await replaceHandler.Handle(
            new ReplaceStaffWorkAssignmentCommand(
                created.AssignmentId,
                replacementStaffContext.UserId!.Value,
                Reason: "Đổi ca",
                Note: "Nhân viên thay thế nhận ca"),
            CancellationToken.None);

        result.OriginalAssignment.Status.ShouldBe(StaffWorkAssignmentStatus.Replaced);
        result.OriginalAssignment.ShiftState.ShouldBe("Replaced");
        result.ReplacementAssignment.StaffUserId.ShouldBe(replacementStaffContext.UserId.Value);
        result.ReplacementAssignment.Boat.ShouldNotBeNull().BoatId.ShouldBe(boat.Id);
        result.ReplacementAssignment.StartAt.ShouldBe(created.StartAt);
        result.ReplacementAssignment.EndAt.ShouldBe(created.EndAt);

        var originalScheduleHandler = new GetMyStaffWorkAssignmentsQueryHandler(
            context,
            originalStaffContext,
            new FixedTimeProvider(now));
        var originalSchedule = await originalScheduleHandler.Handle(
            new GetMyStaffWorkAssignmentsQuery(new DateOnly(2030, 1, 3), new DateOnly(2030, 1, 3)),
            CancellationToken.None);
        originalSchedule.ShouldBeEmpty();

        var replacementScheduleHandler = new GetMyStaffWorkAssignmentsQueryHandler(
            context,
            replacementStaffContext,
            new FixedTimeProvider(now));
        var replacementSchedule = await replacementScheduleHandler.Handle(
            new GetMyStaffWorkAssignmentsQuery(new DateOnly(2030, 1, 3), new DateOnly(2030, 1, 3)),
            CancellationToken.None);
        replacementSchedule.Single().StaffUserId.ShouldBe(replacementStaffContext.UserId.Value);
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

    [Test]
    public async Task StaffTripsReturnsBoatTripsWithinAssignedShift()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var adminContext = await SeatFlowTestData.SeedAdminAsync(context);
        var staffContext = await SeatFlowTestData.SeedStaffAsync(context, StaffType.OnBoard);
        var boat = Boat("WB-01");
        var otherBoat = Boat("WB-02");
        var stationA = Station("BD");
        var stationB = Station("LD");
        var route = Route("R1", stationA, stationB);
        var inShiftTrip = Trip(
            "T-IN",
            route,
            boat,
            new DateTimeOffset(2030, 1, 2, 9, 0, 0, TimeSpan.FromHours(7)),
            new DateTimeOffset(2030, 1, 2, 10, 0, 0, TimeSpan.FromHours(7)));
        var outsideShiftTrip = Trip(
            "T-OUT",
            route,
            boat,
            new DateTimeOffset(2030, 1, 2, 18, 0, 0, TimeSpan.FromHours(7)),
            new DateTimeOffset(2030, 1, 2, 19, 0, 0, TimeSpan.FromHours(7)));
        var otherBoatTrip = Trip(
            "T-OTHER",
            route,
            otherBoat,
            new DateTimeOffset(2030, 1, 2, 9, 0, 0, TimeSpan.FromHours(7)),
            new DateTimeOffset(2030, 1, 2, 10, 0, 0, TimeSpan.FromHours(7)));
        context.AddRange(boat, otherBoat, stationA, stationB, route, inShiftTrip, outsideShiftTrip, otherBoatTrip);
        await context.SaveChangesAsync();

        var createHandler = new CreateStaffWorkAssignmentCommandHandler(context, adminContext, TimeProvider.System);
        await createHandler.Handle(
            new CreateStaffWorkAssignmentCommand(
                staffContext.UserId!.Value,
                StaffWorkAssignmentType.Boat,
                BoatId: boat.Id,
                StartAt: new DateTimeOffset(2030, 1, 2, 8, 0, 0, TimeSpan.FromHours(7)),
                EndAt: new DateTimeOffset(2030, 1, 2, 16, 0, 0, TimeSpan.FromHours(7))),
            CancellationToken.None);

        var handler = new GetMyStaffTripsQueryHandler(
            context,
            staffContext,
            new FixedTimeProvider(new DateTimeOffset(2030, 1, 2, 3, 0, 0, TimeSpan.Zero)));

        var result = await handler.Handle(new GetMyStaffTripsQuery(new DateOnly(2030, 1, 2)), CancellationToken.None);

        var trip = result.Single();
        trip.TripId.ShouldBe(inShiftTrip.Id);
        trip.AssignmentType.ShouldBe(StaffWorkAssignmentType.Boat);
        trip.BoatId.ShouldBe(boat.Id);
        trip.AssignmentShiftState.ShouldBe("Active");
    }

    [Test]
    public async Task StaffTripsReturnsStationTripsWithinAssignedShift()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var managerContext = await SeatFlowTestData.SeedManagerAsync(context);
        var staffContext = await SeatFlowTestData.SeedStaffAsync(context, StaffType.Ground);
        var boat = Boat("WB-01");
        var assignedStation = Station("BD");
        var otherStation = Station("LD");
        var throughAssignedStationRoute = Route("R1", assignedStation, otherStation);
        var otherRoute = Route("R2", otherStation);
        var includedTrip = Trip(
            "T-STATION",
            throughAssignedStationRoute,
            boat,
            new DateTimeOffset(2030, 1, 2, 9, 0, 0, TimeSpan.FromHours(7)),
            new DateTimeOffset(2030, 1, 2, 10, 0, 0, TimeSpan.FromHours(7)));
        var excludedTrip = Trip(
            "T-OTHER",
            otherRoute,
            boat,
            new DateTimeOffset(2030, 1, 2, 9, 0, 0, TimeSpan.FromHours(7)),
            new DateTimeOffset(2030, 1, 2, 10, 0, 0, TimeSpan.FromHours(7)));
        var managerUser = context.Users.Single(x => x.Id == managerContext.UserId!.Value);
        var staffUser = context.Users.Single(x => x.Id == staffContext.UserId!.Value);
        context.AddRange(
            boat,
            assignedStation,
            otherStation,
            throughAssignedStationRoute,
            otherRoute,
            includedTrip,
            excludedTrip,
            StationAssignment(managerUser, assignedStation, managerUser.Id),
            StationAssignment(staffUser, assignedStation, managerUser.Id));
        await context.SaveChangesAsync();

        var createHandler = new CreateStaffWorkAssignmentCommandHandler(context, managerContext, TimeProvider.System);
        await createHandler.Handle(
            new CreateStaffWorkAssignmentCommand(
                staffContext.UserId!.Value,
                StaffWorkAssignmentType.Station,
                StationId: assignedStation.Id,
                StartAt: new DateTimeOffset(2030, 1, 2, 8, 0, 0, TimeSpan.FromHours(7)),
                EndAt: new DateTimeOffset(2030, 1, 2, 16, 0, 0, TimeSpan.FromHours(7))),
            CancellationToken.None);

        var handler = new GetMyStaffTripsQueryHandler(
            context,
            staffContext,
            new FixedTimeProvider(new DateTimeOffset(2030, 1, 2, 3, 0, 0, TimeSpan.Zero)));

        var result = await handler.Handle(new GetMyStaffTripsQuery(new DateOnly(2030, 1, 2)), CancellationToken.None);

        var trip = result.Single();
        trip.TripId.ShouldBe(includedTrip.Id);
        trip.AssignmentType.ShouldBe(StaffWorkAssignmentType.Station);
        trip.StationId.ShouldBe(assignedStation.Id);
    }

    [Test]
    public async Task TripDetailReturnsStaffAndPassengerCountsByTripStopAndSegment()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var adminContext = await SeatFlowTestData.SeedAdminAsync(context);
        var onBoardStaffContext = await SeatFlowTestData.SeedStaffAsync(context, StaffType.OnBoard);
        var groundStaffContext = await SeatFlowTestData.SeedStaffAsync(context, StaffType.Ground);
        var admin = context.Users.Single(x => x.Id == adminContext.UserId!.Value);
        var onBoardStaff = context.Users.Single(x => x.Id == onBoardStaffContext.UserId!.Value);
        var groundStaff = context.Users.Single(x => x.Id == groundStaffContext.UserId!.Value);
        var boat = Boat("WB-01");
        var stationA = Station("A");
        var stationB = Station("B");
        var stationC = Station("C");
        var route = Route("R1", stationA, stationB, stationC);
        var departure = new DateTimeOffset(2030, 1, 2, 8, 0, 0, TimeSpan.FromHours(7)).ToUniversalTime();
        var arrival = departure.AddHours(1);
        var trip = Trip("T-DETAIL", route, boat, departure, arrival);
        var stopA = TripStop(trip, stationA, 1, departure, departure);
        var stopB = TripStop(trip, stationB, 2, departure.AddMinutes(20), departure.AddMinutes(20));
        var stopC = TripStop(trip, stationC, 3, arrival, arrival);
        trip.TripStops.Add(stopA);
        trip.TripStops.Add(stopB);
        trip.TripStops.Add(stopC);
        var onBoardAssignment = Assignment(
            onBoardStaff,
            admin,
            StaffWorkAssignmentType.Boat,
            departure.AddMinutes(-10),
            arrival.AddMinutes(10),
            boat,
            station: null,
            tripStop: null);
        var scannerAssignment = Assignment(
            groundStaff,
            admin,
            StaffWorkAssignmentType.Station,
            departure.AddMinutes(10),
            departure.AddMinutes(35),
            boat: null,
            stationB,
            stopB);
        var booking = new Booking
        {
            UserId = Guid.NewGuid(),
            Trip = trip,
            TripId = trip.Id,
            BookingCode = "BK-DETAIL",
            ContactName = "Passenger",
            ContactPhone = "0900000000",
            BookingStatus = BookingStatus.Confirmed,
            PaymentStatus = "Paid",
            SubtotalAmount = 10000,
            TotalAmount = 10000,
            DepositAmount = 10000,
            RemainingAmount = 0
        };
        booking.Passengers.Add(new BookingPassenger
        {
            Booking = booking,
            BookingId = booking.Id,
            Trip = trip,
            TripId = trip.Id,
            FullName = "Passenger A C",
            FromStationId = stationA.Id,
            ToStationId = stationC.Id,
            FromStopOrder = 1,
            ToStopOrder = 3,
            PassengerType = "ADULT"
        });
        booking.Passengers.Add(new BookingPassenger
        {
            Booking = booking,
            BookingId = booking.Id,
            Trip = trip,
            TripId = trip.Id,
            FullName = "Passenger B",
            FromStationId = stationB.Id,
            ToStationId = stationC.Id,
            FromStopOrder = 2,
            ToStopOrder = 3,
            PassengerType = "ADULT"
        });
        context.AddRange(
            boat,
            stationA,
            stationB,
            stationC,
            route,
            trip,
            onBoardAssignment,
            scannerAssignment,
            booking);
        await context.SaveChangesAsync();

        var result = await new GetTripDetailQueryHandler(context, new FixedTimeProvider(departure.AddMinutes(15)))
            .Handle(new GetTripDetailQuery(trip.Id), CancellationToken.None);

        result.Boat.ShouldNotBeNull().VesselId.ShouldBe(boat.Id);
        result.OnBoardStaff.ShouldNotBeNull().Single().StaffUserId.ShouldBe(onBoardStaff.Id);
        result.TotalPassengerCount.ShouldBe(2);
        var stopADto = result.Stops.Single(x => x.TripStopId == stopA.Id);
        var stopBDto = result.Stops.Single(x => x.TripStopId == stopB.Id);
        var stopCDto = result.Stops.Single(x => x.TripStopId == stopC.Id);
        stopADto.BoardingPassengerCount.ShouldBe(1);
        stopADto.AlightingPassengerCount.ShouldBe(0);
        stopADto.OnboardPassengerCount.ShouldBe(1);
        stopADto.SegmentPassengerCount.ShouldBe(1);
        stopBDto.BoardingPassengerCount.ShouldBe(1);
        stopBDto.AlightingPassengerCount.ShouldBe(0);
        stopBDto.OnboardPassengerCount.ShouldBe(2);
        stopBDto.SegmentPassengerCount.ShouldBe(2);
        stopBDto.ScanningStaff.ShouldNotBeNull().Single().StaffUserId.ShouldBe(groundStaff.Id);
        stopCDto.BoardingPassengerCount.ShouldBe(0);
        stopCDto.AlightingPassengerCount.ShouldBe(2);
        stopCDto.OnboardPassengerCount.ShouldBe(0);
        stopCDto.SegmentPassengerCount.ShouldBe(0);

        var list = await new GetTripListQueryHandler(context, new FixedTimeProvider(departure.AddMinutes(15)))
            .Handle(new GetTripListQuery(null, null, null), CancellationToken.None);
        list.Single(x => x.TripId == trip.Id).TotalPassengerCount.ShouldBe(2);
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

    private static Route Route(string code, params Station[] stations)
    {
        var route = new Route
        {
            RouteCode = code,
            RouteName = code,
            RouteType = RouteTypes.Regular,
            Status = "Active"
        };

        var stopOrder = 1;
        foreach (var station in stations)
        {
            route.RouteStops.Add(new RouteStop
            {
                RouteId = route.Id,
                Route = route,
                StationId = station.Id,
                Station = station,
                StopOrder = stopOrder++
            });
        }

        return route;
    }

    private static Trip Trip(
        string code,
        Route route,
        Boat boat,
        DateTimeOffset departureTime,
        DateTimeOffset arrivalTime) =>
        new()
        {
            TripCode = code,
            TripType = TripTypes.Regular,
            TripStatus = TripStatus.Scheduled,
            RouteId = route.Id,
            Route = route,
            BoatId = boat.Id,
            Boat = boat,
            OperatingDate = DateOnly.FromDateTime(departureTime.ToOffset(TimeSpan.FromHours(7)).DateTime),
            DepartureTime = departureTime,
            ArrivalTime = arrivalTime,
            CapacitySnapshot = boat.SeatCount
        };

    private static TripStop TripStop(
        Trip trip,
        Station station,
        int stopOrder,
        DateTimeOffset plannedArrival,
        DateTimeOffset plannedDeparture) =>
        new()
        {
            Trip = trip,
            TripId = trip.Id,
            Station = station,
            StationId = station.Id,
            StopOrder = stopOrder,
            PlannedArrivalTime = stopOrder == 1 ? null : plannedArrival,
            PlannedDepartureTime = stopOrder == 3 ? null : plannedDeparture
        };

    private static StaffWorkAssignment Assignment(
        User staff,
        User assignedBy,
        StaffWorkAssignmentType assignmentType,
        DateTimeOffset startAt,
        DateTimeOffset endAt,
        Boat? boat,
        Station? station,
        TripStop? tripStop) =>
        new()
        {
            StaffUser = staff,
            StaffUserId = staff.Id,
            AssignedByUser = assignedBy,
            AssignedByUserId = assignedBy.Id,
            AssignmentType = assignmentType,
            Boat = boat,
            BoatId = boat?.Id,
            Station = station,
            StationId = station?.Id,
            TripStop = tripStop,
            TripStopId = tripStop?.Id,
            WorkingDate = DateOnly.FromDateTime(startAt.ToOffset(TimeSpan.FromHours(7)).DateTime),
            StartAt = startAt,
            EndAt = endAt,
            AssignedAt = startAt.AddHours(-1),
            Status = StaffWorkAssignmentStatus.Scheduled
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
