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
        var departureTime = new DateTimeOffset(2030, 1, 1, 8, 0, 0, TimeSpan.FromHours(7));
        var existingTrip = new Trip
        {
            Route = route,
            RouteId = route.Id,
            TripCode = "TR-EXISTING",
            OperatingDate = DateOnly.FromDateTime(departureTime.Date),
            DepartureTime = departureTime.ToUniversalTime(),
            ArrivalTime = departureTime.AddMinutes(30).ToUniversalTime(),
            CapacitySnapshot = 50,
            TripStatus = TripStatus.Scheduled
        };

        var boat = BoatWithSeats("BOAT-1", seatCount: 3);

        context.AddRange(route, existingTrip, boat);
        await context.SaveChangesAsync();

        var exception = await Should.ThrowAsync<ValidationException>(() =>
            new CreateTripCommandHandler(context)
                .Handle(new CreateTripCommand("R1", "BOAT-1", DateOnly.FromDateTime(departureTime.Date), departureTime), CancellationToken.None));

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
        var departureTime = new DateTimeOffset(2030, 1, 1, 8, 0, 0, TimeSpan.FromHours(7));
        var existingTrip = new Trip
        {
            Route = route,
            RouteId = route.Id,
            TripCode = "TR-CANCELLED",
            OperatingDate = DateOnly.FromDateTime(departureTime.Date),
            DepartureTime = departureTime.ToUniversalTime(),
            ArrivalTime = departureTime.AddMinutes(30).ToUniversalTime(),
            CapacitySnapshot = 50,
            TripStatus = TripStatus.Cancelled
        };

        var boat = BoatWithSeats("BOAT-1", seatCount: 3);

        context.AddRange(route, existingTrip, boat);
        await context.SaveChangesAsync();

        var result = await new CreateTripCommandHandler(context)
            .Handle(new CreateTripCommand("R1", "BOAT-1", DateOnly.FromDateTime(departureTime.Date), departureTime), CancellationToken.None);

        result.RouteName.ShouldBe("R1");
    }

    [Test]
    public async Task CreateTripTakesCapacityFromActiveSeatsOfBoat()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var route = Route("R1", Station("A", "Ben A"), Station("B", "Ben B"));
        var departureTime = new DateTimeOffset(2030, 1, 1, 8, 0, 0, TimeSpan.FromHours(7));

        // 5 ghe nhung 2 ghe bi vo hieu hoa -> suc chua phai la 3, khong phai SeatCount cua tau.
        var boat = BoatWithSeats("BOAT-1", seatCount: 5, inactiveSeatCount: 2);

        context.AddRange(route, boat);
        await context.SaveChangesAsync();

        var result = await new CreateTripCommandHandler(context)
            .Handle(new CreateTripCommand("R1", "BOAT-1", DateOnly.FromDateTime(departureTime.Date), departureTime), CancellationToken.None);

        result.CapacitySnapshot.ShouldBe(3);
        context.Set<TripSeat>().Count(x => x.TripId == result.TripId).ShouldBe(3);
    }

    [Test]
    public async Task CreateTripFailsWhenBoatHasNoActiveSeats()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var route = Route("R1", Station("A", "Ben A"), Station("B", "Ben B"));
        var departureTime = new DateTimeOffset(2030, 1, 1, 8, 0, 0, TimeSpan.FromHours(7));
        var boat = BoatWithSeats("BOAT-1", seatCount: 2, inactiveSeatCount: 2);

        context.AddRange(route, boat);
        await context.SaveChangesAsync();

        var exception = await Should.ThrowAsync<ValidationException>(() =>
            new CreateTripCommandHandler(context)
                .Handle(new CreateTripCommand("R1", "BOAT-1", DateOnly.FromDateTime(departureTime.Date), departureTime), CancellationToken.None));

        exception.Errors["boatCode"].ShouldContain("Boat has no active seats.");
    }

    private static Boat BoatWithSeats(string code, int seatCount, int inactiveSeatCount = 0)
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
                IsActive = i >= inactiveSeatCount
            });
        }

        return boat;
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
