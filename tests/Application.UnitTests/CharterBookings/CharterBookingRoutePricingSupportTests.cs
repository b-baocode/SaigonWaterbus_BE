using NUnit.Framework;
using NetTopologySuite.Geometries;
using SaigonWaterbus.Application.Common.Exceptions;
using SaigonWaterbus.Application.CharterBookings;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using Shouldly;

namespace SaigonWaterbus.Application.UnitTests.CharterBookings;

public class CharterBookingRoutePricingSupportTests
{
    [Test]
    public void HourlyRentalChargesByEstimatedRouteMinutes()
    {
        var estimate = RouteEstimate(125);

        var chargeableHours = CharterBookingRoutePricingSupport.ResolveChargeableDurationValue(
            BoatRentalUnit.Hour,
            requestedDurationValue: 1,
            estimate);

        chargeableHours.ShouldBe(2.083m);
    }

    [Test]
    public void TryExtractLegGeometryCutsMiddleSectionOfSourceRoute()
    {
        var route = new Route
        {
            RouteCode = "R-GEO",
            RouteName = "R",
            RouteGeometry = new LineString(
            [
                new Coordinate(106, 10),
                new Coordinate(106.01, 10),
                new Coordinate(106.02, 10),
                new Coordinate(106.03, 10)
            ])
        };
        var from = new Station { StationName = "B", Latitude = 10, Longitude = 106.01m };
        var to = new Station { StationName = "C", Latitude = 10, Longitude = 106.02m };

        var coordinates = CharterBookingRoutePricingSupport.TryExtractLegGeometry(route, from, to);

        coordinates.ShouldNotBeNull();
        coordinates[0].X.ShouldBe(106.01, 0.0001);
        coordinates[^1].X.ShouldBe(106.02, 0.0001);
        coordinates.ShouldAllBe(c => c.X >= 106.0099 && c.X <= 106.0201);
    }

    [Test]
    public void TryExtractLegGeometryReversesWhenLegGoesAgainstSourceDirection()
    {
        var route = new Route
        {
            RouteCode = "R-GEO",
            RouteName = "R",
            RouteGeometry = new LineString(
            [
                new Coordinate(106, 10),
                new Coordinate(106.01, 10),
                new Coordinate(106.02, 10)
            ])
        };
        var from = new Station { StationName = "C", Latitude = 10, Longitude = 106.02m };
        var to = new Station { StationName = "A", Latitude = 10, Longitude = 106 };

        var coordinates = CharterBookingRoutePricingSupport.TryExtractLegGeometry(route, from, to);

        coordinates.ShouldNotBeNull();
        coordinates[0].X.ShouldBe(106.02, 0.0001);
        coordinates[^1].X.ShouldBe(106.0, 0.0001);
    }

    [Test]
    public void TryExtractLegGeometryReturnsNullWhenStationTooFarFromSourceGeometry()
    {
        var route = new Route
        {
            RouteCode = "R-GEO",
            RouteName = "R",
            RouteGeometry = new LineString(
            [
                new Coordinate(106, 10),
                new Coordinate(106.01, 10)
            ])
        };
        var from = new Station { StationName = "A", Latitude = 10, Longitude = 106 };
        var farAway = new Station { StationName = "X", Latitude = 11, Longitude = 107 };

        CharterBookingRoutePricingSupport.TryExtractLegGeometry(route, from, farAway).ShouldBeNull();
    }

    [Test]
    public void HourlyPriceSubtractsFreeStayMinutesAndUsesExactChargeableMinutes()
    {
        var fromStation = new Station
        {
            StationName = "Bến A",
            Latitude = 10,
            Longitude = 106
        };
        var stopStation = new Station
        {
            StationName = "Bến B",
            Latitude = 10,
            Longitude = 106.001m
        };
        var route = new Route
        {
            RouteCode = "R-TEST",
            RouteName = "Bến A - Bến B",
            RouteGeometry = new LineString(
            [
                new Coordinate(106, 10),
                new Coordinate(106.001, 10)
            ]),
            RouteStops =
            [
                new RouteStop { Station = fromStation, StationId = fromStation.Id, StopOrder = 1 },
                new RouteStop { Station = stopStation, StationId = stopStation.Id, StopOrder = 2 }
            ]
        };
        var booking = new Booking
        {
            RentalUnit = BoatRentalUnit.Hour,
            DurationValue = 1,
            FromStation = fromStation,
            ItineraryStops =
            [
                new BookingItineraryStop
                {
                    Station = stopStation,
                    StayDurationMinutes = 116,
                    StopOrder = 1
                }
            ]
        };
        var boat = new Boat
        {
            HourlyRentalPrice = 1_000_000m
        };

        var pricing = CharterBookingRoutePricingSupport.EstimatePrice(
            booking,
            boat,
            BoatRentalUnit.Hour,
            requestedDurationValue: 1,
            relatedRoutes: [route]);

        pricing.RouteEstimate.EstimatedDurationMinutes.ShouldBe(126);
        pricing.RouteEstimate.FreeStayMinutes.ShouldBe(30);
        pricing.RouteEstimate.ChargeableStayMinutes.ShouldBe(86);
        pricing.RouteEstimate.ChargeableDurationMinutes.ShouldBe(96);
        pricing.ChargeableDurationValue.ShouldBe(1.600m);
        pricing.SubtotalAmount.ShouldBe(1_600_000m);

        var leg = pricing.RouteEstimate.Legs.Single();
        leg.MatchedRouteId.ShouldBe(route.Id);
        leg.MatchedRouteCode.ShouldBe("R-TEST");
        leg.MatchedRouteName.ShouldBe("Bến A - Bến B");

        var dto = CharterBookingRoutePricingSupport.ToDto(
            pricing.RouteEstimate,
            BoatRentalUnit.Hour,
            requestedDurationValue: 1);
        dto.MatchedRouteId.ShouldBe(route.Id);
        dto.MatchedRouteCode.ShouldBe("R-TEST");
        dto.MatchedRouteName.ShouldBe("Bến A - Bến B");
        dto.Legs.Single().MatchedRouteId.ShouldBe(route.Id);
        dto.Legs.Single().MatchedRouteCode.ShouldBe("R-TEST");
        dto.Legs.Single().MatchedRouteName.ShouldBe("Bến A - Bến B");
    }

    [Test]
    public void HourlyRentalKeepsRequestedHoursWhenRequestedDurationIsLonger()
    {
        var estimate = RouteEstimate(125);

        var chargeableHours = CharterBookingRoutePricingSupport.ResolveChargeableDurationValue(
            BoatRentalUnit.Hour,
            requestedDurationValue: 4,
            estimate);

        chargeableHours.ShouldBe(4m);
    }

    [Test]
    public void DailyRentalUsesRequestedDays()
    {
        var estimate = RouteEstimate(721);

        var chargeableDays = CharterBookingRoutePricingSupport.ResolveChargeableDurationValue(
            BoatRentalUnit.Day,
            requestedDurationValue: 2,
            estimate);
        var dto = CharterBookingRoutePricingSupport.ToDto(
            estimate,
            BoatRentalUnit.Day,
            requestedDurationValue: 2);

        chargeableDays.ShouldBe(2);
        dto.ChargeableDurationMinutes.ShouldBe(1840);
    }

    [Test]
    public void IncompleteRouteEstimateFallsBackToRequestedDuration()
    {
        var estimate = new CharterBookingRouteEstimate(
            [],
            null,
            EstimatedTravelMinutes: 0,
            EstimatedStayMinutes: 45,
            FreeStayMinutes: 30,
            ChargeableStayMinutes: 15,
            EstimatedBufferMinutes: 0,
            EstimatedDurationMinutes: 45,
            ChargeableDurationMinutes: 15,
            HasCompleteDistanceEstimate: false,
            HasCompleteTravelTimeEstimate: false);

        var chargeableHours = CharterBookingRoutePricingSupport.ResolveChargeableDurationValue(
            BoatRentalUnit.Hour,
            requestedDurationValue: 2,
            estimate);

        chargeableHours.ShouldBe(2m);
    }

    [Test]
    public void HourlyAutoPricingRequiresCompleteDistanceEstimate()
    {
        var estimate = new CharterBookingRouteEstimate(
            [],
            null,
            EstimatedTravelMinutes: 0,
            EstimatedStayMinutes: 45,
            FreeStayMinutes: 30,
            ChargeableStayMinutes: 15,
            EstimatedBufferMinutes: 0,
            EstimatedDurationMinutes: 45,
            ChargeableDurationMinutes: 15,
            HasCompleteDistanceEstimate: false,
            HasCompleteTravelTimeEstimate: false);

        var exception = Should.Throw<ValidationException>(() =>
            CharterBookingRoutePricingSupport.EnsureCanAutoPrice(BoatRentalUnit.Hour, estimate));

        exception.Errors["subtotalAmount"].Single()
            .ShouldContain("chưa có route");
    }

    [Test]
    public void DailyAutoPricingAllowsIncompleteDistanceEstimate()
    {
        var estimate = new CharterBookingRouteEstimate(
            [],
            null,
            EstimatedTravelMinutes: 0,
            EstimatedStayMinutes: 45,
            FreeStayMinutes: 30,
            ChargeableStayMinutes: 15,
            EstimatedBufferMinutes: 0,
            EstimatedDurationMinutes: 45,
            ChargeableDurationMinutes: 15,
            HasCompleteDistanceEstimate: false,
            HasCompleteTravelTimeEstimate: false);

        Should.NotThrow(() =>
            CharterBookingRoutePricingSupport.EnsureCanAutoPrice(BoatRentalUnit.Day, estimate));
    }

    private static CharterBookingRouteEstimate RouteEstimate(int estimatedDurationMinutes) =>
        new(
            [],
            TotalDistanceKm: 10,
            EstimatedTravelMinutes: estimatedDurationMinutes,
            EstimatedStayMinutes: 0,
            FreeStayMinutes: 0,
            ChargeableStayMinutes: 0,
            EstimatedBufferMinutes: 0,
            EstimatedDurationMinutes: estimatedDurationMinutes,
            ChargeableDurationMinutes: estimatedDurationMinutes,
            HasCompleteDistanceEstimate: true,
            HasCompleteTravelTimeEstimate: true);
}
