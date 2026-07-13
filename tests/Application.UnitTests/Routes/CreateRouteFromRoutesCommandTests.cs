using NUnit.Framework;
using SaigonWaterbus.Application.Common.Exceptions;
using SaigonWaterbus.Application.Routes;
using SaigonWaterbus.Application.UnitTests.TestInfrastructure;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using Shouldly;

namespace SaigonWaterbus.Application.UnitTests.Routes;

public class CreateRouteFromRoutesCommandTests
{
    [Test]
    public async Task CreatesRouteWithStopsFromConnectedSourceRoutes()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var stationA = Station("A");
        var stationB = Station("B");
        var stationC = Station("C");
        var routeAB = Route("A-B", stationA, stationB, 6);
        var routeBC = Route("B-C", stationB, stationC, 8);

        context.AddRange(routeAB, routeBC);
        await context.SaveChangesAsync();

        var result = await new CreateRouteFromRoutesCommandHandler(context)
            .Handle(new CreateRouteFromRoutesCommand(
                "A-B-C",
                "A - B - C",
                RouteTypes.Regular,
                null,
                [routeAB.Id, routeBC.Id]), CancellationToken.None);

        result.RouteCode.ShouldBe("A-B-C");
        result.RouteType.ShouldBe(RouteTypes.Regular);
        result.Stops.Select(stop => stop.StationCode).ShouldBe(["A", "B", "C"]);
        result.Stops.Select(stop => stop.StopOrder).ShouldBe([1, 2, 3]);
        result.Stops.Select(stop => stop.StandardTravelMin).ShouldBe([null, 6, 8]);

        var savedRoute = context.Routes.Single(route => route.RouteCode == "A-B-C");
        savedRoute.IsBookable.ShouldBeTrue();
    }

    [Test]
    public async Task RejectsDisconnectedSourceRoutes()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var stationA = Station("A");
        var stationB = Station("B");
        var stationC = Station("C");
        var stationD = Station("D");
        var routeAB = Route("A-B", stationA, stationB, 6);
        var routeCD = Route("C-D", stationC, stationD, 8);

        context.AddRange(routeAB, routeCD);
        await context.SaveChangesAsync();

        var exception = await Should.ThrowAsync<ValidationException>(() =>
            new CreateRouteFromRoutesCommandHandler(context)
                .Handle(new CreateRouteFromRoutesCommand(
                    "A-B-C-D",
                    "A - B - C - D",
                    RouteTypes.Regular,
                    null,
                    [routeAB.Id, routeCD.Id]), CancellationToken.None));

        exception.Errors["sourceRouteIds"][0]
            .ShouldContain("ben cuoi cua route truoc phai trung ben dau cua route sau");
    }

    private static Station Station(string code) =>
        new()
        {
            StationCode = code,
            StationName = $"Ben {code}",
            Status = StationStatus.Active
        };

    private static Route Route(string code, Station from, Station to, int travelMin)
    {
        var route = new Route
        {
            RouteCode = code,
            RouteName = code,
            RouteType = RouteTypes.CharterReference,
            Status = "Active",
            IsBookable = false
        };

        route.RouteStops =
        [
            new RouteStop
            {
                Route = route,
                Station = from,
                StationId = from.Id,
                StopOrder = 1,
                StandardTravelMin = null,
                IsPickupAllowed = true,
                IsDropoffAllowed = false
            },
            new RouteStop
            {
                Route = route,
                Station = to,
                StationId = to.Id,
                StopOrder = 2,
                StandardTravelMin = travelMin,
                IsPickupAllowed = false,
                IsDropoffAllowed = true
            }
        ];

        return route;
    }
}
