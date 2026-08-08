using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using SaigonWaterbus.Application.Trips;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using SaigonWaterbus.Infrastructure.Data;
using Shouldly;
using WaterbusRoute = SaigonWaterbus.Domain.Entities.Route;

namespace SaigonWaterbus.Application.UnitTests.Trips;

public sealed class GenerateTripsSchedulePlannerTests
{
    private static readonly TimeSpan VietnamOffset = TimeSpan.FromHours(7);

    [Test]
    public async Task Fixed_departure_times_report_warning_when_boat_cannot_return_to_start_station()
    {
        await using var context = CreateContext();
        await SeedScheduleContextAsync(context, RouteTypes.Regular, SeatSetupType.FullStandard, sameStartAndEndStation: false);

        var result = await BuildPlanAsync(
            context,
            departureTimes:
            [
                new TimeOnly(8, 0),
                new TimeOnly(9, 20),
                new TimeOnly(10, 0)
            ]);

        result.TripsToAdd.Select(x => LocalTime(x.DepartureTime)).ShouldBe([
            new TimeOnly(8, 0),
            new TimeOnly(10, 0)
        ]);
        result.SkippedBoatBusy.ShouldBe(1);

        var warning = result.SkippedItems.Single();
        LocalTime(warning.RequestedDepartureTime).ShouldBe(new TimeOnly(9, 20));
        LocalTime(warning.EarliestAllowedDepartureTime!.Value).ShouldBe(new TimeOnly(9, 55));
        warning.Reason.ShouldContain("Chuyến mới chỉ được khởi hành sớm nhất lúc 09:55");
    }

    [Test]
    public async Task Continuous_schedule_for_loop_route_uses_arrival_plus_layover()
    {
        await using var context = CreateContext();
        await SeedScheduleContextAsync(context, RouteTypes.SightseeingLoop, SeatSetupType.StandardAndVip, sameStartAndEndStation: true);

        var result = await BuildPlanAsync(
            context,
            startTime: new TimeOnly(8, 0),
            endTime: new TimeOnly(10, 0),
            intervalMinutes: 15);

        result.TripsToAdd.Select(x => LocalTime(x.DepartureTime)).ShouldBe([
            new TimeOnly(8, 0),
            new TimeOnly(9, 5)
        ]);
        result.SkippedItems.ShouldBeEmpty();
    }

    [Test]
    public async Task Continuous_schedule_for_regular_route_adds_reposition_time_to_start_station()
    {
        await using var context = CreateContext();
        await SeedScheduleContextAsync(context, RouteTypes.Regular, SeatSetupType.FullStandard, sameStartAndEndStation: false);

        var result = await BuildPlanAsync(
            context,
            startTime: new TimeOnly(8, 0),
            endTime: new TimeOnly(10, 0),
            intervalMinutes: 15);

        result.TripsToAdd.Select(x => LocalTime(x.DepartureTime)).ShouldBe([
            new TimeOnly(8, 0),
            new TimeOnly(9, 55)
        ]);
        result.SkippedItems.ShouldBeEmpty();
    }

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new ApplicationDbContext(options);
    }

    private static async Task<GenerateTripsSchedulePlan> BuildPlanAsync(
        ApplicationDbContext context,
        IReadOnlyList<TimeOnly>? departureTimes = null,
        TimeOnly? startTime = null,
        TimeOnly? endTime = null,
        int? intervalMinutes = null)
    {
        var command = new PreviewTripsScheduleCommand(
            "R01",
            "BOAT-01",
            departureTimes,
            new DateOnly(2030, 1, 1),
            new DateOnly(2030, 1, 1),
            StartTime: startTime,
            EndTime: endTime,
            IntervalMinutes: intervalMinutes);

        return await new GenerateTripsSchedulePlanner(context)
            .BuildPlanAsync(command, CancellationToken.None);
    }

    private static async Task SeedScheduleContextAsync(
        ApplicationDbContext context,
        string routeType,
        SeatSetupType seatSetupType,
        bool sameStartAndEndStation)
    {
        var startStation = new Station
        {
            StationCode = "A",
            StationName = "Station A"
        };
        var endStation = sameStartAndEndStation
            ? startStation
            : new Station
            {
                StationCode = "B",
                StationName = "Station B"
            };

        var route = new WaterbusRoute
        {
            RouteCode = "R01",
            RouteName = "Route 01",
            RouteType = routeType,
            Status = "Active",
            IsBookable = true,
            EstimatedDurationMin = 50
        };
        route.RouteStops.Add(new RouteStop
        {
            Route = route,
            Station = startStation,
            StationId = startStation.Id,
            StopOrder = 1
        });
        route.RouteStops.Add(new RouteStop
        {
            Route = route,
            Station = endStation,
            StationId = endStation.Id,
            StopOrder = 2,
            StandardTravelMin = 50,
            DistanceFromPreviousKm = 5
        });

        var boat = new Boat
        {
            Code = "BOAT-01",
            Name = "Boat 01",
            Status = BoatStatus.Active,
            ServiceType = BoatServiceType.Passenger,
            SeatSetupType = seatSetupType,
            SeatsConfigured = true,
            SeatCount = 1,
            NumberOfDecks = 1
        };
        boat.Seats.Add(new Seat
        {
            Boat = boat,
            BoatId = boat.Id,
            Code = "A1",
            Deck = 1,
            Row = "A",
            Column = 1,
            IsActive = true
        });

        var admin = new User
        {
            FullName = "Admin",
            Status = UserStatus.Active
        };
        var shiftStart = new DateTimeOffset(2030, 1, 1, 7, 0, 0, VietnamOffset).ToUniversalTime();
        var shiftEnd = new DateTimeOffset(2030, 1, 1, 12, 0, 0, VietnamOffset).ToUniversalTime();
        var workingDate = new DateOnly(2030, 1, 1);

        for (var i = 1; i <= 2; i++)
        {
            var staff = new User
            {
                FullName = $"OnBoard {i}",
                StaffType = StaffType.OnBoard,
                Status = UserStatus.Active
            };
            context.Users.Add(staff);
            context.StaffWorkAssignments.Add(new StaffWorkAssignment
            {
                StaffUser = staff,
                StaffUserId = staff.Id,
                AssignedByUser = admin,
                AssignedByUserId = admin.Id,
                AssignmentType = StaffWorkAssignmentType.Boat,
                Boat = boat,
                BoatId = boat.Id,
                WorkingDate = workingDate,
                StartAt = shiftStart,
                EndAt = shiftEnd,
                Status = StaffWorkAssignmentStatus.Scheduled,
                AssignedAt = shiftStart
            });
        }

        context.Users.Add(admin);
        context.Stations.Add(startStation);
        if (!sameStartAndEndStation)
        {
            context.Stations.Add(endStation);
        }
        context.Routes.Add(route);
        context.Boats.Add(boat);

        await context.SaveChangesAsync(CancellationToken.None);
    }

    private static TimeOnly LocalTime(DateTimeOffset value) =>
        TimeOnly.FromDateTime(value.ToOffset(VietnamOffset).DateTime);
}
