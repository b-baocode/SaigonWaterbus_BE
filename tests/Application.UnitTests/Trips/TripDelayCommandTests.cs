using NUnit.Framework;
using SaigonWaterbus.Application.Common.Exceptions;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Application.Trips;
using SaigonWaterbus.Application.UnitTests.TestInfrastructure;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using SaigonWaterbus.Infrastructure.Data;
using Shouldly;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.UnitTests.Trips;

public class TripDelayCommandTests
{
    [Test]
    public async Task ResumeDelayCascadesWhenSameBoatNextTripCannotMeetTurnaroundBuffer()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var staffContext = await SeatFlowTestData.SeedStaffAsync(context, StaffType.OnBoard);
        var sourceDeparture = new DateTimeOffset(2030, 1, 1, 8, 0, 0, TimeSpan.Zero);
        var sourceTrip = SeedTrip(context, "TR-SOURCE", sourceDeparture, "BOAT-1");
        var futureSameBoat = SeedTrip(context, "TR-FUTURE", sourceDeparture.AddMinutes(90), "BOAT-1", sourceTrip.Boat!);
        var otherBoat = SeedTrip(context, "TR-OTHER", sourceDeparture.AddMinutes(100), "BOAT-2");
        await AddOnBoardAssignmentAsync(
            context,
            staffContext.UserId!.Value,
            sourceTrip.BoatId!.Value,
            sourceDeparture.AddHours(-1),
            sourceDeparture.AddHours(5));
        await context.SaveChangesAsync();

        var delayStartedAt = sourceDeparture.AddMinutes(30);
        var startHandler = new StartTripDelayCommandHandler(
            context,
            staffContext,
            new FixedTimeProvider(delayStartedAt),
            new RecordingTripDelayRealtimeNotifier());

        var started = await startHandler.Handle(
            new StartTripDelayCommand(sourceTrip.Id, "Dừng tại bến B", StartStopOrder: 2),
            CancellationToken.None);

        started.DelayInfo.ShouldNotBeNull();
        started.DelayInfo.IsDelayActive.ShouldBeTrue();
        started.DelayInfo.DelayMinutes.ShouldBe(0);
        sourceTrip.TripStatus.ShouldBe(TripStatus.Delayed);
        sourceTrip.DelayStartedAt.ShouldBe(delayStartedAt);
        sourceTrip.TripStops.Single(x => x.StopOrder == 2).AdjustedDepartureTime.ShouldBeNull();

        var resumeHandler = new ResumeTripDelayCommandHandler(
            context,
            staffContext,
            new FixedTimeProvider(delayStartedAt.AddMinutes(20)),
            new RecordingTripDelayRealtimeNotifier());

        var resumed = await resumeHandler.Handle(
            new ResumeTripDelayCommand(sourceTrip.Id, "Tàu tiếp tục hành trình"),
            CancellationToken.None);

        resumed.DelayInfo.ShouldNotBeNull();
        resumed.DelayInfo.DelayMinutes.ShouldBe(20);
        resumed.DelayInfo.IsDelayActive.ShouldBeFalse();
        resumed.DelayInfo.DelayPropagationMinutes.ShouldBe(10);
        resumed.AffectedTrips.Select(x => x.TripCode).ShouldBe(["TR-FUTURE"]);
        resumed.AffectedTrips.Single().AddedDelayMinutes.ShouldBe(10);

        sourceTrip.DelayMinutes.ShouldBe(20);
        sourceTrip.DelayPropagationMinutes.ShouldBe(10);
        sourceTrip.AdjustedDepartureTime.ShouldBeNull();
        sourceTrip.AdjustedArrivalTime.ShouldBe(sourceTrip.ArrivalTime.AddMinutes(20));
        sourceTrip.TripStops.Single(x => x.StopOrder == 1).AdjustedDepartureTime.ShouldBeNull();
        var sourceStopB = sourceTrip.TripStops.Single(x => x.StopOrder == 2);
        sourceStopB.AdjustedArrivalTime.ShouldBe(sourceStopB.PlannedArrivalTime!.Value.AddMinutes(20));
        sourceStopB.AdjustedDepartureTime.ShouldBe(sourceStopB.PlannedDepartureTime!.Value.AddMinutes(20));
        var sourceStopC = sourceTrip.TripStops.Single(x => x.StopOrder == 3);
        sourceStopC.AdjustedArrivalTime.ShouldBe(sourceStopC.PlannedArrivalTime!.Value.AddMinutes(20));
        resumed.Trip.AdjustedDepartureTime.ShouldBeNull();
        resumed.Trip.AdjustedArrivalTime.ShouldBe(sourceTrip.ArrivalTime.AddMinutes(20));
        resumed.Trip.Stops.Single(x => x.StopOrder == 3)
            .ScheduledArrival.ShouldBe(sourceStopC.PlannedArrivalTime);
        resumed.Trip.Stops.Single(x => x.StopOrder == 3)
            .AdjustedArrival.ShouldBe(sourceStopC.PlannedArrivalTime!.Value.AddMinutes(20));

        futureSameBoat.DelayMinutes.ShouldBe(10);
        futureSameBoat.DelayReason!.ShouldContain("TR-SOURCE");
        futureSameBoat.AdjustedDepartureTime.ShouldBe(futureSameBoat.DepartureTime.AddMinutes(10));
        futureSameBoat.TripStops.Single(x => x.StopOrder == 1)
            .AdjustedDepartureTime.ShouldBe(futureSameBoat.TripStops.Single(x => x.StopOrder == 1)
                .PlannedDepartureTime!.Value.AddMinutes(10));

        otherBoat.DelayMinutes.ShouldBe(0);
        otherBoat.AdjustedDepartureTime.ShouldBeNull();
    }

    [Test]
    public async Task ResumeDelayWithinFifteenMinutesStillCascadesWhenNextTripIsTooClose()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var staffContext = await SeatFlowTestData.SeedStaffAsync(context, StaffType.OnBoard);
        var sourceDeparture = new DateTimeOffset(2030, 1, 1, 8, 0, 0, TimeSpan.Zero);
        var sourceTrip = SeedTrip(context, "TR-SOURCE", sourceDeparture, "BOAT-1");
        var futureSameBoat = SeedTrip(context, "TR-FUTURE", sourceDeparture.AddMinutes(80), "BOAT-1", sourceTrip.Boat!);
        await AddOnBoardAssignmentAsync(
            context,
            staffContext.UserId!.Value,
            sourceTrip.BoatId!.Value,
            sourceDeparture.AddHours(-1),
            sourceDeparture.AddHours(5));
        await context.SaveChangesAsync();

        var delayStartedAt = sourceDeparture.AddMinutes(30);
        await new StartTripDelayCommandHandler(
                context,
                staffContext,
                new FixedTimeProvider(delayStartedAt))
            .Handle(new StartTripDelayCommand(sourceTrip.Id, "Dừng tại bến B", StartStopOrder: 2), CancellationToken.None);

        var resumed = await new ResumeTripDelayCommandHandler(
                context,
                staffContext,
                new FixedTimeProvider(delayStartedAt.AddMinutes(10)))
            .Handle(new ResumeTripDelayCommand(sourceTrip.Id, "Tàu tiếp tục hành trình"), CancellationToken.None);

        resumed.DelayInfo.ShouldNotBeNull();
        resumed.DelayInfo.DelayMinutes.ShouldBe(10);
        resumed.DelayInfo.DelayPropagationMinutes.ShouldBe(10);
        resumed.AffectedTrips.Select(x => x.TripCode).ShouldBe(["TR-FUTURE"]);
        resumed.AffectedTrips.Single().AddedDelayMinutes.ShouldBe(10);

        sourceTrip.TripStops.Single(x => x.StopOrder == 2)
            .AdjustedDepartureTime.ShouldBe(sourceDeparture.AddMinutes(35 + 10));
        futureSameBoat.DelayMinutes.ShouldBe(10);
        futureSameBoat.AdjustedDepartureTime.ShouldBe(futureSameBoat.DepartureTime.AddMinutes(10));
    }

    [Test]
    public async Task StaffWithoutActiveBoatAssignmentCannotStartDelay()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var staffContext = await SeatFlowTestData.SeedStaffAsync(context, StaffType.OnBoard);
        var trip = SeedTrip(
            context,
            "TR-SOURCE",
            new DateTimeOffset(2030, 1, 1, 8, 0, 0, TimeSpan.Zero),
            "BOAT-1");
        await context.SaveChangesAsync();

        var handler = new StartTripDelayCommandHandler(
            context,
            staffContext,
            new FixedTimeProvider(trip.DepartureTime.AddMinutes(30)));

        var ex = await Should.ThrowAsync<ValidationException>(() =>
            handler.Handle(new StartTripDelayCommand(trip.Id, "Delay", StartStopOrder: 2), CancellationToken.None));

        ex.Errors["staffWorkAssignment"].Single().ShouldContain("chưa có ca OnBoard");
    }

    [Test]
    public async Task ManagerCannotStartDelay()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var managerContext = await SeatFlowTestData.SeedManagerAsync(context);
        var trip = SeedTrip(
            context,
            "TR-SOURCE",
            new DateTimeOffset(2030, 1, 1, 8, 0, 0, TimeSpan.Zero),
            "BOAT-1");
        await context.SaveChangesAsync();

        var handler = new StartTripDelayCommandHandler(
            context,
            managerContext,
            new FixedTimeProvider(trip.DepartureTime.AddMinutes(30)));

        await Should.ThrowAsync<ForbiddenAccessException>(() =>
            handler.Handle(new StartTripDelayCommand(trip.Id, "Delay", StartStopOrder: 2), CancellationToken.None));
    }

    private static Trip SeedTrip(
        ApplicationDbContext context,
        string tripCode,
        DateTimeOffset departureTime,
        string boatCode,
        Boat? boat = null)
    {
        var stationA = Station($"{tripCode}-A", "Bến A");
        var stationB = Station($"{tripCode}-B", "Bến B");
        var stationC = Station($"{tripCode}-C", "Bến C");
        var route = Route($"R-{tripCode}", stationA, stationB, stationC);
        boat ??= BoatWithSeats(boatCode, seatCount: 3);
        var trip = new Trip
        {
            Route = route,
            RouteId = route.Id,
            Boat = boat,
            BoatId = boat.Id,
            TripCode = tripCode,
            OperatingDate = DateOnly.FromDateTime(departureTime.Date),
            DepartureTime = departureTime,
            ArrivalTime = departureTime.AddMinutes(65),
            CapacitySnapshot = 3,
            TripStatus = TripStatus.InProgress
        };

        AddTripStop(trip, stationA, 1, null, departureTime);
        AddTripStop(trip, stationB, 2, departureTime.AddMinutes(30), departureTime.AddMinutes(35));
        AddTripStop(trip, stationC, 3, departureTime.AddMinutes(65), null);

        context.Add(trip);
        return trip;
    }

    private static void AddTripStop(
        Trip trip,
        Station station,
        int stopOrder,
        DateTimeOffset? plannedArrival,
        DateTimeOffset? plannedDeparture)
    {
        trip.TripStops.Add(new TripStop
        {
            Trip = trip,
            TripId = trip.Id,
            Station = station,
            StationId = station.Id,
            StopOrder = stopOrder,
            StayDurationMinutes = stopOrder == 2 ? 5 : 0,
            PlannedArrivalTime = plannedArrival,
            PlannedDepartureTime = plannedDeparture
        });
    }

    private static Route Route(string code, params Station[] stations)
    {
        var route = new Route
        {
            RouteCode = code,
            RouteName = code,
            Status = "Active"
        };

        var stopOrder = 1;
        foreach (var station in stations)
        {
            route.RouteStops.Add(new RouteStop
            {
                Route = route,
                Station = station,
                StationId = station.Id,
                StopOrder = stopOrder,
                StandardTravelMin = stopOrder == 1 ? null : 30,
                DistanceFromPreviousKm = stopOrder == 1 ? null : 3m
            });
            stopOrder++;
        }

        return route;
    }

    private static Boat BoatWithSeats(string code, int seatCount)
    {
        var boat = new Boat
        {
            Code = code,
            Name = code,
            Status = BoatStatus.Active,
            SeatCount = seatCount,
            NumberOfDecks = 1,
            SeatSetupType = SeatSetupType.FullStandard,
            SeatsConfigured = true
        };

        for (var i = 0; i < seatCount; i++)
        {
            boat.Seats.Add(new Seat
            {
                Boat = boat,
                BoatId = boat.Id,
                Code = $"A{i + 1}",
                SeatTypeCode = "STANDARD",
                Deck = 1,
                Row = "A",
                Column = i + 1,
                IsActive = true
            });
        }

        return boat;
    }

    private static async Task AddOnBoardAssignmentAsync(
        ApplicationDbContext context,
        Guid staffUserId,
        Guid boatId,
        DateTimeOffset startAt,
        DateTimeOffset endAt)
    {
        context.StaffWorkAssignments.Add(new StaffWorkAssignment
        {
            StaffUserId = staffUserId,
            AssignmentType = StaffWorkAssignmentType.Boat,
            BoatId = boatId,
            WorkingDate = DateOnly.FromDateTime(startAt.Date),
            StartAt = startAt,
            EndAt = endAt,
            Status = StaffWorkAssignmentStatus.Scheduled,
            AssignedByUserId = staffUserId,
            AssignedAt = startAt.AddHours(-1)
        });
        await context.SaveChangesAsync();
    }

    private static Station Station(string code, string name) =>
        new()
        {
            StationCode = code,
            StationName = name,
            Status = StationStatus.Active
        };

    private sealed class RecordingTripDelayRealtimeNotifier : ITripDelayRealtimeNotifier
    {
        public List<TripDelayRealtimeEvent> Published { get; } = [];

        public Task PublishUpdatedAsync(
            TripDelayRealtimeEvent change,
            CancellationToken cancellationToken)
        {
            Published.Add(change);
            return Task.CompletedTask;
        }
    }
}
