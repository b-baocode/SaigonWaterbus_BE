using NUnit.Framework;
using SaigonWaterbus.Application.Routes;
using SaigonWaterbus.Application.UnitTests.TestInfrastructure;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;
using Shouldly;

namespace SaigonWaterbus.Application.UnitTests.Routes;

public class GetRouteListQueryTests
{
    [Test]
    public async Task CharterSourceUsageOnlyReturnsGpsAndSightseeingRoutes()
    {
        await using var context = SeatFlowTestData.CreateContext();
        context.AddRange(
            Route("BUS-01", RouteTypes.Regular),
            Route("GPS-01", RouteTypes.CharterReference),
            Route("SIG-01", RouteTypes.SightseeingLoop),
            Route("CH-CB-20260714-ABC", RouteTypes.Charter));
        await context.SaveChangesAsync();

        var result = await new GetRouteListQueryHandler(context)
            .Handle(new GetRouteListQuery("charter-source"), CancellationToken.None);

        result.Select(x => x.RouteCode).ShouldBe(["GPS-01", "SIG-01"]);
        result.Select(x => x.RouteLabel).ShouldBe([
            RoutePresentationSupport.GpsLabel,
            RoutePresentationSupport.SightseeingLabel
        ]);
        result.All(x => x.IsSelectableForCharterQuote).ShouldBeTrue();
        result.Any(x => x.IsGeneratedForBooking).ShouldBeFalse();
    }

    [Test]
    public async Task DefaultListMarksGeneratedCharterRoutesAsNotSelectableForQuote()
    {
        await using var context = SeatFlowTestData.CreateContext();
        context.AddRange(
            Route("GPS-01", RouteTypes.CharterReference),
            Route("CH-CB-20260714-ABC", RouteTypes.Charter));
        await context.SaveChangesAsync();

        var result = await new GetRouteListQueryHandler(context)
            .Handle(new GetRouteListQuery(), CancellationToken.None);

        var gpsRoute = result.Single(x => x.RouteCode == "GPS-01");
        gpsRoute.RouteLabel.ShouldBe(RoutePresentationSupport.GpsLabel);
        gpsRoute.IsSelectableForCharterQuote.ShouldBeTrue();
        gpsRoute.IsGeneratedForBooking.ShouldBeFalse();

        var generatedCharterRoute = result.Single(x => x.RouteCode == "CH-CB-20260714-ABC");
        generatedCharterRoute.RouteLabel.ShouldBe(RoutePresentationSupport.CharterLabel);
        generatedCharterRoute.IsSelectableForCharterQuote.ShouldBeFalse();
        generatedCharterRoute.IsGeneratedForBooking.ShouldBeTrue();
    }

    private static Route Route(string code, string routeType) =>
        new()
        {
            RouteCode = code,
            RouteName = code,
            RouteType = routeType,
            Status = "Active",
            IsBookable = RouteTypes.IsBookableByDefault(routeType)
        };
}
