using NetTopologySuite.Geometries;
using NUnit.Framework;
using SaigonWaterbus.Application.Routes;
using Shouldly;

namespace SaigonWaterbus.Application.UnitTests.Routes;

public class RouteGeometryResponseSupportTests
{
    [Test]
    public void ToCoordinatesUsesGeoJsonLongitudeLatitudeOrderAndPreservesPathOrder()
    {
        var geometry = new LineString([
            new Coordinate(106.7041, 10.7721),
            new Coordinate(106.7082, 10.7763)]);

        var coordinates = RouteGeometryResponseSupport.ToCoordinates(geometry);

        coordinates.ShouldNotBeNull();
        coordinates.ShouldBe([
            [106.7041, 10.7721],
            [106.7082, 10.7763]
        ]);
    }

    [Test]
    public void ToCoordinatesReturnsNullWhenGeometryIsMissing() =>
        RouteGeometryResponseSupport.ToCoordinates(null).ShouldBeNull();
}
