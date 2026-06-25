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
    public async Task CreateTripRejectsSameRouteDepartureAtSameTime()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var stationA = Station("A", "Ben A");
        var stationB = Station("B", "Ben B");
        var route = Route("R1", stationA, stationB);
        var service = SeatFlowTestData.Service("SVC1");
        var departureTime = new DateTimeOffset(2030, 1, 1, 8, 0, 0, TimeSpan.FromHours(7));
        var existingTrip = new Trip
        {
            Route = route,
            RouteId = route.Id,
            TripCode = "TR-EXISTING",
            OperatingDate = DateOnly.FromDateTime(departureTime.Date),
            DepartureTime = departureTime,
            ArrivalTime = departureTime.AddMinutes(30),
            CapacitySnapshot = 50,
            TripStatus = TripStatus.Scheduled
        };

        context.AddRange(route, service, existingTrip);
        await context.SaveChangesAsync();

        var exception = await Should.ThrowAsync<ValidationException>(() =>
            new CreateTripCommandHandler(context)
                .Handle(new CreateTripCommand("R1", 40, DateOnly.FromDateTime(departureTime.Date), departureTime), CancellationToken.None));

        exception.Errors["departureTime"]
            .ShouldContain("Tuyến đã có chuyến tàu xuất phát trong cùng thời điểm.");
    }

    [Test]
    public async Task CreateTripAllowsSameRouteDepartureWhenExistingTripIsCancelled()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var stationA = Station("A", "Ben A");
        var stationB = Station("B", "Ben B");
        var route = Route("R1", stationA, stationB);
        var service = SeatFlowTestData.Service("SVC1");
        var departureTime = new DateTimeOffset(2030, 1, 1, 8, 0, 0, TimeSpan.FromHours(7));
        var existingTrip = new Trip
        {
            Route = route,
            RouteId = route.Id,
            TripCode = "TR-CANCELLED",
            OperatingDate = DateOnly.FromDateTime(departureTime.Date),
            DepartureTime = departureTime,
            ArrivalTime = departureTime.AddMinutes(30),
            CapacitySnapshot = 50,
            TripStatus = TripStatus.Cancelled
        };

        context.AddRange(route, service, existingTrip);
        await context.SaveChangesAsync();

        var result = await new CreateTripCommandHandler(context)
            .Handle(new CreateTripCommand("R1", 40, DateOnly.FromDateTime(departureTime.Date), departureTime), CancellationToken.None);

        result.RouteName.ShouldBe("R1");
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
