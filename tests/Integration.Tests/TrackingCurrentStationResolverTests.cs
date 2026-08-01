using NUnit.Framework;
using SaigonWaterbus.Application.Tracking;
using SaigonWaterbus.Domain.Entities;
using Shouldly;

namespace SaigonWaterbus.Integration.Tests;

public sealed class TrackingCurrentStationResolverTests
{
    private const double RadiusKm = 0.08d;

    [Test]
    public void ResolveUsesNearestStationWhenRequestedCodeIsStale()
    {
        var stations = new[]
        {
            Station("ST-BD", 10.7752488m, 106.7073457m),
            Station("ST-TT", 10.7890000m, 106.7300000m)
        };

        var station = TrackingCurrentStationResolver.Resolve(
            stations,
            "ST-TT",
            10.7752301m,
            106.7072821m,
            RadiusKm);

        station.ShouldNotBeNull();
        station.StationCode.ShouldBe("ST-BD");
    }

    [Test]
    public void ResolveClearsRequestedCodeWhenGpsIsNotNearThatStation()
    {
        var stations = new[]
        {
            Station("ST-BD", 10.7752488m, 106.7073457m),
            Station("ST-TT", 10.7890000m, 106.7300000m)
        };

        var station = TrackingCurrentStationResolver.Resolve(
            stations,
            "ST-TT",
            10.7767663m,
            106.7096303m,
            RadiusKm);

        station.ShouldBeNull();
    }

    [Test]
    public void ResolveReturnsNullWhenNoStationCodeAndGpsIsOutsideRadius()
    {
        var stations = new[]
        {
            Station("ST-BD", 10.7752488m, 106.7073457m)
        };

        var station = TrackingCurrentStationResolver.Resolve(
            stations,
            null,
            10.8228540m,
            106.7287005m,
            RadiusKm);

        station.ShouldBeNull();
    }

    private static Station Station(string code, decimal lat, decimal lng) =>
        new()
        {
            StationCode = code,
            StationName = code,
            Latitude = lat,
            Longitude = lng
        };
}
