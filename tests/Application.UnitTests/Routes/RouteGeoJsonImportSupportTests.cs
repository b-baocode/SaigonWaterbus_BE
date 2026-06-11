using NetTopologySuite.Geometries;
using NUnit.Framework;
using Shouldly;
using SaigonWaterbus.Application.Routes;

namespace SaigonWaterbus.Application.UnitTests.Routes;

public class RouteGeoJsonImportSupportTests
{
    [Test]
    public void ParseOnlyIncludesWaterwaySegmentsAndFerryTerminalPoints()
    {
        const string geoJson =
            """
            {
              "type": "FeatureCollection",
              "features": [
                {
                  "type": "Feature",
                  "properties": { "@id": "way/1", "waterway": "river", "name": "Song A" },
                  "geometry": { "type": "LineString", "coordinates": [[106.0, 10.0], [106.1, 10.1]] }
                },
                {
                  "type": "Feature",
                  "properties": { "@id": "way/2", "natural": "water", "water": "river" },
                  "geometry": { "type": "LineString", "coordinates": [[106.0, 10.0], [106.2, 10.2]] }
                },
                {
                  "type": "Feature",
                  "properties": { "@id": "node/1", "amenity": "ferry_terminal", "name": "Ben A" },
                  "geometry": { "type": "Point", "coordinates": [106.0, 10.0] }
                },
                {
                  "type": "Feature",
                  "properties": { "@id": "node/2", "amenity": "parking", "name": "Bai xe" },
                  "geometry": { "type": "Point", "coordinates": [106.1, 10.1] }
                }
              ]
            }
            """;

        var parsed = RouteGeoJsonImportSupport.Parse(geoJson);

        parsed.WaterwaySegments.Count.ShouldBe(1);
        parsed.WaterwaySegments[0].OsmId.ShouldBe("way/1");
        parsed.StationCandidates.Count.ShouldBe(1);
        parsed.StationCandidates[0].Name.ShouldBe("Ben A");
    }

    [Test]
    public void CreateRepresentativePointReturnsPointOnLongestViaGeometry()
    {
        var shortLine = new LineString(
            [new Coordinate(106.0000, 10.0000), new Coordinate(106.0010, 10.0000)])
        { SRID = 4326 };
        var longLine = new LineString(
            [new Coordinate(106.0100, 10.0000), new Coordinate(106.0200, 10.0000), new Coordinate(106.0300, 10.0000)])
        { SRID = 4326 };

        var representative = RouteGeoJsonImportSupport.CreateRepresentativePoint([shortLine, longLine]);

        representative.X.ShouldBeGreaterThan(106.0150);
        representative.X.ShouldBeLessThan(106.0250);
        representative.Y.ShouldBe(10.0000, 0.000001);
    }

    [Test]
    public void BuildRouteGeometryUsesViaWaypointToChooseSpecificBranch()
    {
        var a = new Coordinate(106.0000, 10.0000);
        var b = new Coordinate(106.0100, 10.0000);
        var c = new Coordinate(106.0200, 10.0000);
        var d = new Coordinate(106.0300, 10.0000);
        var e = new Coordinate(106.0150, 10.0100);

        var river = new LineString([a, b, c, d]) { SRID = 4326 };
        var canal = new LineString([b, e, c]) { SRID = 4326 };
        var waterways = new[] { river, canal };

        var riverGeometry = RouteGeoJsonImportSupport.BuildRouteGeometry(
            waterways,
            [new Point(a) { SRID = 4326 }, RouteGeoJsonImportSupport.CreateRepresentativePoint([river]), new Point(d) { SRID = 4326 }]);

        var canalGeometry = RouteGeoJsonImportSupport.BuildRouteGeometry(
            waterways,
            [new Point(a) { SRID = 4326 }, RouteGeoJsonImportSupport.CreateRepresentativePoint([canal]), new Point(d) { SRID = 4326 }]);

        ContainsCoordinate(riverGeometry, e).ShouldBeFalse();
        ContainsCoordinate(canalGeometry, e).ShouldBeTrue();
        canalGeometry.ToText().ShouldNotBe(riverGeometry.ToText());
    }

    [Test]
    public void BuildRouteGeometryRequiresAtLeastTwoWaypoints()
    {
        var river = new LineString(
            [new Coordinate(106.0000, 10.0000), new Coordinate(106.0100, 10.0000)])
        { SRID = 4326 };

        var ex = Should.Throw<SaigonWaterbus.Application.Common.Exceptions.ValidationException>(() =>
            RouteGeoJsonImportSupport.BuildRouteGeometry(
                [river],
                [new Point(106.0000, 10.0000) { SRID = 4326 }]));

        ex.Errors.Single().Value.Single().ShouldContain("Can it nhat 2 waypoint");
    }

    private static bool ContainsCoordinate(LineString lineString, Coordinate expected) =>
        lineString.Coordinates.Any(coordinate =>
            Math.Abs(coordinate.X - expected.X) < 0.000001
            && Math.Abs(coordinate.Y - expected.Y) < 0.000001);
}
