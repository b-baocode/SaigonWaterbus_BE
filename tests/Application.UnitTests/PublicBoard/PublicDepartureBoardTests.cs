using NUnit.Framework;
using SaigonWaterbus.Application.PublicBoard;
using SaigonWaterbus.Application.UnitTests.TestInfrastructure;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using Shouldly;

namespace SaigonWaterbus.Application.UnitTests.PublicBoard;

public class PublicDepartureBoardTests
{
    [Test]
    public async Task DepartureBoardShowsBoardingRegularTripAtStation()
    {
        var now = new DateTimeOffset(2030, 1, 1, 8, 55, 0, TimeSpan.Zero);
        await using var context = SeatFlowTestData.CreateContext();
        var (_, from, _, trip) = AddTrip(context, new DateTimeOffset(2030, 1, 1, 9, 0, 0, TimeSpan.Zero));
        await context.SaveChangesAsync();

        var result = await Handler(context, now).Handle(
            new GetPublicDepartureBoardQuery(StationCode: from.StationCode),
            CancellationToken.None);

        result.Count.ShouldBe(1);
        result[0].TripId.ShouldBe(trip.Id);
        result[0].StationCode.ShouldBe(from.StationCode);
        result[0].DisplayStatus.ShouldBe("Boarding");
        result[0].MinutesToDeparture.ShouldBe(7);
    }

    [Test]
    public async Task DepartureBoardShowsArrivingSoonForDownstreamStation()
    {
        var now = new DateTimeOffset(2030, 1, 1, 9, 12, 0, TimeSpan.Zero);
        await using var context = SeatFlowTestData.CreateContext();
        var (_, _, to, trip) = AddTrip(context, new DateTimeOffset(2030, 1, 1, 9, 0, 0, TimeSpan.Zero));
        await context.SaveChangesAsync();

        var result = await Handler(context, now).Handle(
            new GetPublicDepartureBoardQuery(StationId: to.Id),
            CancellationToken.None);

        result.Count.ShouldBe(1);
        result[0].TripId.ShouldBe(trip.Id);
        result[0].StationCode.ShouldBe(to.StationCode);
        result[0].DisplayStatus.ShouldBe("ArrivingSoon");
        result[0].MinutesToArrival.ShouldBe(8);
    }

    [Test]
    public async Task DepartureBoardHidesTripsDepartedOutsideWindow()
    {
        var now = new DateTimeOffset(2030, 1, 1, 9, 30, 0, TimeSpan.Zero);
        await using var context = SeatFlowTestData.CreateContext();
        var (_, from, _, _) = AddTrip(context, new DateTimeOffset(2030, 1, 1, 9, 0, 0, TimeSpan.Zero));
        await context.SaveChangesAsync();

        var result = await Handler(context, now).Handle(
            new GetPublicDepartureBoardQuery(StationCode: from.StationCode, IncludeDepartedMinutes: 20),
            CancellationToken.None);

        result.ShouldBeEmpty();
    }

    private static GetPublicDepartureBoardQueryHandler Handler(
        Infrastructure.Data.ApplicationDbContext context,
        DateTimeOffset now) =>
        new(context, new FixedTimeProvider(now));

    private static (Route Route, Station From, Station To, Trip Trip) AddTrip(
        Infrastructure.Data.ApplicationDbContext context,
        DateTimeOffset departureTime)
    {
        var from = Station("BD", "Bach Dang");
        var to = Station("LD", "Linh Dong");
        var route = new Route
        {
            RouteCode = "R01",
            RouteName = "Route 01",
            Status = "Active"
        };
        var fromStop = new RouteStop
        {
            Route = route,
            RouteId = route.Id,
            Station = from,
            StationId = from.Id,
            StopOrder = 1
        };
        var toStop = new RouteStop
        {
            Route = route,
            RouteId = route.Id,
            Station = to,
            StationId = to.Id,
            StopOrder = 2
        };
        route.RouteStops = [fromStop, toStop];

        var trip = new Trip
        {
            Route = route,
            RouteId = route.Id,
            TripCode = "TR-20300101-R01-0001",
            OperatingDate = DateOnly.FromDateTime(departureTime.Date),
            DepartureTime = departureTime,
            ArrivalTime = departureTime.AddMinutes(20),
            CapacitySnapshot = 50,
            TripStatus = TripStatus.Scheduled
        };
        trip.TripStops =
        [
            new TripStop
            {
                Trip = trip,
                TripId = trip.Id,
                RouteStop = fromStop,
                RouteStopId = fromStop.Id,
                StopOrder = 1,
                ScheduledArrival = departureTime,
                ScheduledDeparture = departureTime.AddMinutes(2),
                StopStatus = "Scheduled"
            },
            new TripStop
            {
                Trip = trip,
                TripId = trip.Id,
                RouteStop = toStop,
                RouteStopId = toStop.Id,
                StopOrder = 2,
                ScheduledArrival = departureTime.AddMinutes(20),
                ScheduledDeparture = departureTime.AddMinutes(22),
                StopStatus = "Scheduled"
            }
        ];

        context.Add(route);
        context.Add(trip);
        return (route, from, to, trip);
    }

    private static Station Station(string code, string name) =>
        new()
        {
            StationCode = code,
            StationName = name,
            Status = StationStatus.Active
        };

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
