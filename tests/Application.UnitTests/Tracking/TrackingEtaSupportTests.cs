using NUnit.Framework;
using SaigonWaterbus.Application.Tracking;
using Shouldly;

namespace SaigonWaterbus.Application.UnitTests.Tracking;

public sealed class TrackingEtaSupportTests
{
    private static readonly DateTimeOffset Now =
        new(2030, 1, 1, 12, 0, 0, TimeSpan.FromHours(7));

    [Test]
    public void CalculatesDistanceAndEtaFromMovingSpeed()
    {
        var result = TrackingEtaSupport.Resolve(
            10m, 106m, 10m, 106.00913m, 30m, null, Now);

        result.RemainingDistanceKm!.Value.ShouldBe(1.002m, 0.01m);
        result.RemainingMinutes.ShouldBe(2);
    }

    [Test]
    public void MovingNearStationStillReportsOneMinuteUntilDocked()
    {
        var result = TrackingEtaSupport.Resolve(
            10m, 106m, 10m, 106.0009m, 30m, null, Now);

        result.RemainingMinutes.ShouldBe(1);
    }

    [Test]
    public void DockingThresholdReportsZeroMinutes()
    {
        var result = TrackingEtaSupport.Resolve(
            10m, 106m, 10m, 106.0005m, 5m, null, Now);

        result.RemainingMinutes.ShouldBe(0);
    }

    [Test]
    public void FallsBackToPlannedArrivalWhenSpeedUnavailable()
    {
        var result = TrackingEtaSupport.Resolve(
            10m, 106m, 10m, 106.01m, null, Now.AddMinutes(7), Now);

        result.RemainingMinutes.ShouldBe(7);
    }

    [Test]
    public void SuppliedEtaIsPreserved()
    {
        var result = TrackingEtaSupport.Resolve(
            10m, 106m, 10m, 106.01m, 30m, null, Now, 2.4m, 8);

        result.RemainingDistanceKm.ShouldBe(2.4m);
        result.RemainingMinutes.ShouldBe(8);
    }
}
