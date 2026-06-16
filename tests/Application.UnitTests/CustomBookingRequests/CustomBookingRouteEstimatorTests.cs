using SaigonWaterbus.Application.CustomBookingRequests;
using SaigonWaterbus.Domain.Entities;
using NUnit.Framework;
using Shouldly;

namespace SaigonWaterbus.Application.UnitTests.CustomBookingRequests;

public class CustomBookingRouteEstimatorTests
{
    [Test]
    public void EstimateCalculatesRouteDistanceTravelStayBufferAndEndTime()
    {
        var fromStation = Station("Start", 10.000000m, 106.000000m);
        var stopStation = Station("Middle", 10.000000m, 106.010000m);
        var toStation = Station("End", 10.000000m, 106.020000m);
        var itineraryStops = new[]
        {
            new CustomBookingItineraryStop
            {
                StopOrder = 1,
                Station = stopStation,
                StationId = stopStation.Id,
                StayDurationMinutes = 90
            }
        };

        var estimate = CustomBookingRouteEstimator.Estimate(
            fromStation,
            itineraryStops,
            toStation,
            new DateOnly(2026, 6, 25),
            new TimeOnly(8, 30),
            vessel: null);

        estimate.Legs.Count.ShouldBe(2);
        estimate.TotalDistanceKm.ShouldNotBeNull();
        estimate.TotalDistanceKm.Value.ShouldBeGreaterThan(2m);
        estimate.AverageSpeedKmh.ShouldBe(13m);
        estimate.MaxSpeedKmh.ShouldBe(28m);
        estimate.EstimatedTravelMinutes.ShouldBe(12);
        estimate.EstimatedStayMinutes.ShouldBe(90);
        estimate.BufferMinutes.ShouldBe(11);
        estimate.EstimatedDurationMinutes.ShouldBe(113);
        estimate.EstimatedEndDate.ShouldBe(new DateOnly(2026, 6, 25));
        estimate.EstimatedEndTime.ShouldBe(new TimeOnly(10, 23));
        estimate.HasCompleteDistanceEstimate.ShouldBeTrue();
        estimate.HasCompleteTravelTimeEstimate.ShouldBeTrue();
    }

    [Test]
    public void EstimateKeepsTravelUnknownWhenStationCoordinatesAreMissing()
    {
        var fromStation = Station("Start", null, null);
        var toStation = Station("End", 10.000000m, 106.020000m);

        var estimate = CustomBookingRouteEstimator.Estimate(
            fromStation,
            Array.Empty<CustomBookingItineraryStop>(),
            toStation,
            new DateOnly(2026, 6, 25),
            new TimeOnly(8, 30),
            vessel: null);

        estimate.TotalDistanceKm.ShouldBeNull();
        estimate.EstimatedTravelMinutes.ShouldBe(0);
        estimate.EstimatedDurationMinutes.ShouldBe(0);
        estimate.EstimatedEndTime.ShouldBeNull();
        estimate.HasCompleteDistanceEstimate.ShouldBeFalse();
        estimate.HasCompleteTravelTimeEstimate.ShouldBeFalse();
    }

    [Test]
    public void EstimateUsesRouteSegmentBeforeCoordinateFallback()
    {
        var fromStation = Station("Start", 10.000000m, 106.000000m);
        var toStation = Station("End", 10.000000m, 106.001000m);
        var segment = new RouteSegment
        {
            RouteId = Guid.NewGuid(),
            FromStationId = fromStation.Id,
            ToStationId = toStation.Id,
            SegmentOrder = 1,
            DistanceKm = 5.5m,
            EstimatedTravelMinutes = 40
        };

        var estimate = CustomBookingRouteEstimator.Estimate(
            fromStation,
            Array.Empty<CustomBookingItineraryStop>(),
            toStation,
            new DateOnly(2026, 6, 25),
            new TimeOnly(8, 30),
            vessel: null,
            [segment]);

        estimate.Legs[0].DistanceKm.ShouldBe(5.5m);
        estimate.Legs[0].TravelMinutes.ShouldBe(26);
        estimate.EstimatedTravelMinutes.ShouldBe(26);
        estimate.BufferMinutes.ShouldBe(3);
        estimate.EstimatedDurationMinutes.ShouldBe(29);
    }

    [Test]
    public void EstimateUsesSegmentsFromAnotherRouteWhenBestRouteDoesNotCoverAllLegs()
    {
        var fromStation = Station("Start", 10.000000m, 106.000000m);
        var stopStation = Station("Middle", 10.000000m, 106.010000m);
        var toStation = Station("End", 10.000000m, 106.020000m);
        var firstRouteId = Guid.NewGuid();
        var secondRouteId = Guid.NewGuid();
        var itineraryStops = new[]
        {
            new CustomBookingItineraryStop
            {
                StopOrder = 1,
                Station = stopStation,
                StationId = stopStation.Id,
                StayDurationMinutes = 0
            }
        };
        var routeSegments = new[]
        {
            new RouteSegment
            {
                RouteId = firstRouteId,
                FromStationId = fromStation.Id,
                ToStationId = stopStation.Id,
                SegmentOrder = 1,
                DistanceKm = 2m,
                EstimatedTravelMinutes = 10
            },
            new RouteSegment
            {
                RouteId = secondRouteId,
                FromStationId = stopStation.Id,
                ToStationId = toStation.Id,
                SegmentOrder = 1,
                DistanceKm = 3m,
                EstimatedTravelMinutes = 20
            }
        };

        var estimate = CustomBookingRouteEstimator.Estimate(
            fromStation,
            itineraryStops,
            toStation,
            new DateOnly(2026, 6, 25),
            new TimeOnly(8, 30),
            vessel: null,
            routeSegments);

        estimate.Legs[0].DistanceKm.ShouldBe(2m);
        estimate.Legs[0].TravelMinutes.ShouldBe(10);
        estimate.Legs[1].DistanceKm.ShouldBe(3m);
        estimate.Legs[1].TravelMinutes.ShouldBe(14);
        estimate.TotalDistanceKm.ShouldBe(5m);
        estimate.EstimatedTravelMinutes.ShouldBe(24);
    }

    [Test]
    public void EstimateRoundTripMatchesCustomBookingResponseExample()
    {
        var fromStation = Station("Bến Ba Son", null, null);
        var stopStation = Station("Bến Bạch Đằng", null, null);
        var routeId = Guid.NewGuid();
        var itineraryStops = new[]
        {
            new CustomBookingItineraryStop
            {
                StopOrder = 1,
                Station = stopStation,
                StationId = stopStation.Id,
                StayDurationMinutes = 90
            }
        };
        var segment = new RouteSegment
        {
            RouteId = routeId,
            FromStationId = fromStation.Id,
            ToStationId = stopStation.Id,
            SegmentOrder = 1,
            DistanceKm = 0.79m
        };

        var estimate = CustomBookingRouteEstimator.Estimate(
            fromStation,
            itineraryStops,
            fromStation,
            new DateOnly(2026, 6, 20),
            new TimeOnly(7, 0),
            vessel: null,
            [segment]);

        estimate.TotalDistanceKm.ShouldBe(1.58m);
        estimate.EstimatedTravelMinutes.ShouldBe(8);
        estimate.EstimatedStayMinutes.ShouldBe(90);
        estimate.BufferMinutes.ShouldBe(10);
        estimate.EstimatedDurationMinutes.ShouldBe(108);
        estimate.EstimatedEndTime.ShouldBe(new TimeOnly(8, 48));
    }

    private static Station Station(string name, decimal? latitude, decimal? longitude) =>
        new()
        {
            Id = Guid.NewGuid(),
            StationCode = name.ToUpperInvariant(),
            StationName = name,
            Latitude = latitude,
            Longitude = longitude
        };
}
