using NUnit.Framework;
using SaigonWaterbus.Application.Common.Exceptions;
using SaigonWaterbus.Application.Trips;
using SaigonWaterbus.Application.UnitTests.TestInfrastructure;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using Shouldly;

namespace SaigonWaterbus.Application.UnitTests.Trips;

public class CreateTripCommandTests
{
    [Test]
    public async Task CreateTripRejectsSameStationDepartureAtSameTime()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var stationA = Station("A", "Ben A");
        var stationB = Station("B", "Ben B");
        var stationC = Station("C", "Ben C");
        var existingRoute = Route("R1", stationA, stationB);
        var newRoute = Route("R2", stationA, stationC);
        var departureTime = new DateTimeOffset(2030, 1, 1, 8, 0, 0, TimeSpan.FromHours(7));
        var existingTrip = new Trip
        {
            Route = existingRoute,
            RouteId = existingRoute.Id,
            TripCode = "TR-EXISTING",
            OperatingDate = DateOnly.FromDateTime(departureTime.Date),
            DepartureTime = departureTime,
            ArrivalTime = departureTime.AddMinutes(30),
            CapacitySnapshot = 50,
            TripStatus = TripStatus.Scheduled
        };
        existingTrip.TripStops =
        [
            new TripStop
            {
                Trip = existingTrip,
                RouteStop = existingRoute.RouteStops.Single(x => x.StopOrder == 1),
                RouteStopId = existingRoute.RouteStops.Single(x => x.StopOrder == 1).Id,
                StopOrder = 1,
                ScheduledArrival = departureTime,
                ScheduledDeparture = departureTime.AddMinutes(2),
                StopStatus = "Scheduled"
            },
            new TripStop
            {
                Trip = existingTrip,
                RouteStop = existingRoute.RouteStops.Single(x => x.StopOrder == 2),
                RouteStopId = existingRoute.RouteStops.Single(x => x.StopOrder == 2).Id,
                StopOrder = 2,
                ScheduledArrival = departureTime.AddMinutes(20),
                ScheduledDeparture = departureTime.AddMinutes(22),
                StopStatus = "Scheduled"
            }
        ];

        context.AddRange(existingRoute, newRoute, existingTrip);
        await context.SaveChangesAsync();

        var exception = await Should.ThrowAsync<ValidationException>(() =>
            new CreateTripCommandHandler(context)
                .Handle(new CreateTripCommand("R2", 40, DateOnly.FromDateTime(departureTime.Date), departureTime), CancellationToken.None));

        exception.Errors["departureTime"]
            .ShouldContain("Bến đã có chuyến tàu xuất phát trong cùng thời điểm.");
    }

    [Test]
    public async Task CreateTripAllowsSameStationDepartureWhenExistingTripIsCancelled()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var stationA = Station("A", "Ben A");
        var stationB = Station("B", "Ben B");
        var stationC = Station("C", "Ben C");
        var existingRoute = Route("R1", stationA, stationB);
        var newRoute = Route("R2", stationA, stationC);
        var departureTime = new DateTimeOffset(2030, 1, 1, 8, 0, 0, TimeSpan.FromHours(7));
        var existingTrip = new Trip
        {
            Route = existingRoute,
            RouteId = existingRoute.Id,
            TripCode = "TR-CANCELLED",
            OperatingDate = DateOnly.FromDateTime(departureTime.Date),
            DepartureTime = departureTime,
            ArrivalTime = departureTime.AddMinutes(30),
            CapacitySnapshot = 50,
            TripStatus = TripStatus.Cancelled
        };
        existingTrip.TripStops =
        [
            new TripStop
            {
                Trip = existingTrip,
                RouteStop = existingRoute.RouteStops.Single(x => x.StopOrder == 1),
                RouteStopId = existingRoute.RouteStops.Single(x => x.StopOrder == 1).Id,
                StopOrder = 1,
                ScheduledArrival = departureTime,
                ScheduledDeparture = departureTime.AddMinutes(2),
                StopStatus = "Scheduled"
            }
        ];

        context.AddRange(existingRoute, newRoute, existingTrip);
        await context.SaveChangesAsync();

        var result = await new CreateTripCommandHandler(context)
            .Handle(new CreateTripCommand("R2", 40, DateOnly.FromDateTime(departureTime.Date), departureTime), CancellationToken.None);

        result.RouteName.ShouldBe("R2");
    }

    private static Station Station(string code, string name) =>
        new()
        {
            StationCode = code,
            StationName = name,
            Status = StationStatus.Active
        };

    private static Route Route(string code, Station from, Station to)
    {
        var route = new Route
        {
            RouteCode = code,
            RouteName = code,
            Status = "Active"
        };
        route.RouteStops =
        [
            new RouteStop
            {
                Route = route,
                Station = from,
                StationId = from.Id,
                StopOrder = 1,
                StandardDwellMin = 2,
                StandardTravelMin = 15
            },
            new RouteStop
            {
                Route = route,
                Station = to,
                StationId = to.Id,
                StopOrder = 2,
                StandardDwellMin = 2,
                StandardTravelMin = 15
            }
        ];

        return route;
    }
}
