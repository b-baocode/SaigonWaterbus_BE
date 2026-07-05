using NUnit.Framework;
using SaigonWaterbus.Application.Common.Exceptions;
using SaigonWaterbus.Application.CharterBookings;
using SaigonWaterbus.Domain.Enums;
using Shouldly;

namespace SaigonWaterbus.Application.UnitTests.CharterBookings;

public class CharterBookingRoutePricingSupportTests
{
    [Test]
    public void HourlyRentalChargesAtLeastEstimatedRouteHours()
    {
        var estimate = RouteEstimate(125);

        var chargeableHours = CharterBookingRoutePricingSupport.ResolveChargeableDurationValue(
            BoatRentalUnit.Hour,
            requestedDurationValue: 1,
            estimate);

        chargeableHours.ShouldBe(3);
    }

    [Test]
    public void HourlyRentalKeepsRequestedHoursWhenRequestedDurationIsLonger()
    {
        var estimate = RouteEstimate(125);

        var chargeableHours = CharterBookingRoutePricingSupport.ResolveChargeableDurationValue(
            BoatRentalUnit.Hour,
            requestedDurationValue: 4,
            estimate);

        chargeableHours.ShouldBe(4);
    }

    [Test]
    public void DailyRentalTreatsTwelveHoursAsOneDay()
    {
        var estimate = RouteEstimate(720);

        var chargeableDays = CharterBookingRoutePricingSupport.ResolveChargeableDurationValue(
            BoatRentalUnit.Day,
            requestedDurationValue: 1,
            estimate);

        chargeableDays.ShouldBe(1);
    }

    [Test]
    public void DailyRentalRoundsAboveTwelveHoursToNextDay()
    {
        var estimate = RouteEstimate(721);

        var chargeableDays = CharterBookingRoutePricingSupport.ResolveChargeableDurationValue(
            BoatRentalUnit.Day,
            requestedDurationValue: 1,
            estimate);

        chargeableDays.ShouldBe(2);
    }

    [Test]
    public void IncompleteRouteEstimateFallsBackToRequestedDuration()
    {
        var estimate = new CharterBookingRouteEstimate(
            [],
            null,
            EstimatedTravelMinutes: 0,
            EstimatedStayMinutes: 45,
            EstimatedBufferMinutes: 0,
            EstimatedDurationMinutes: 45,
            HasCompleteDistanceEstimate: false,
            HasCompleteTravelTimeEstimate: false);

        var chargeableHours = CharterBookingRoutePricingSupport.ResolveChargeableDurationValue(
            BoatRentalUnit.Hour,
            requestedDurationValue: 2,
            estimate);

        chargeableHours.ShouldBe(2);
    }

    [Test]
    public void HourlyAutoPricingRequiresCompleteDistanceEstimate()
    {
        var estimate = new CharterBookingRouteEstimate(
            [],
            null,
            EstimatedTravelMinutes: 0,
            EstimatedStayMinutes: 45,
            EstimatedBufferMinutes: 0,
            EstimatedDurationMinutes: 45,
            HasCompleteDistanceEstimate: false,
            HasCompleteTravelTimeEstimate: false);

        var exception = Should.Throw<ValidationException>(() =>
            CharterBookingRoutePricingSupport.EnsureCanAutoPrice(BoatRentalUnit.Hour, estimate));

        exception.Errors["subtotalAmount"].Single()
            .ShouldContain("chưa có đủ dữ liệu quãng đường");
    }

    [Test]
    public void DailyAutoPricingAllowsIncompleteDistanceEstimate()
    {
        var estimate = new CharterBookingRouteEstimate(
            [],
            null,
            EstimatedTravelMinutes: 0,
            EstimatedStayMinutes: 45,
            EstimatedBufferMinutes: 0,
            EstimatedDurationMinutes: 45,
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
            EstimatedBufferMinutes: 0,
            EstimatedDurationMinutes: estimatedDurationMinutes,
            HasCompleteDistanceEstimate: true,
            HasCompleteTravelTimeEstimate: true);
}
