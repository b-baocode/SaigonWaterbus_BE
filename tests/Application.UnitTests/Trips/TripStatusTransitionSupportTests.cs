using NUnit.Framework;
using SaigonWaterbus.Application.Trips;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using Shouldly;

namespace SaigonWaterbus.Application.UnitTests.Trips;

public class TripStatusTransitionSupportTests
{
    [Test]
    public void BoardingStartsOnlyWithinTenMinutesBeforeDeparture()
    {
        var departure = new DateTimeOffset(2030, 1, 1, 8, 0, 0, TimeSpan.Zero);
        var trip = new Trip { DepartureTime = departure };

        TripStatusTransitionSupport.CanMarkBoarding(
                trip,
                currentStop: null,
                departure.AddMinutes(-17))
            .ShouldBeFalse();

        TripStatusTransitionSupport.CanMarkBoarding(
                trip,
                currentStop: null,
                departure.AddMinutes(-10))
            .ShouldBeTrue();
    }

    [Test]
    public void BoardingUsesAdjustedStopDepartureWhenAvailable()
    {
        var departure = new DateTimeOffset(2030, 1, 1, 8, 0, 0, TimeSpan.Zero);
        var adjustedStopDeparture = departure.AddMinutes(20);
        var trip = new Trip { DepartureTime = departure };
        var tripStop = new TripStop
        {
            PlannedDepartureTime = departure,
            AdjustedDepartureTime = adjustedStopDeparture
        };

        TripStatusTransitionSupport.CanMarkBoarding(
                trip,
                tripStop,
                departure.AddMinutes(-5))
            .ShouldBeFalse();

        TripStatusTransitionSupport.CanMarkBoarding(
                trip,
                tripStop,
                adjustedStopDeparture.AddMinutes(-10))
            .ShouldBeTrue();
    }

    [Test]
    public void DwellCountdownKeepsEarlyArrivalUntilScheduledDeparture()
    {
        var departure = new DateTimeOffset(2030, 1, 1, 8, 0, 0, TimeSpan.Zero);
        var trip = new Trip { DepartureTime = departure };
        var tripStop = new TripStop
        {
            StationId = Guid.NewGuid(),
            StopOrder = 1,
            StopStatus = TripStopStatuses.Arrived,
            ActualArrivalTime = departure.AddMinutes(-17),
            PlannedDepartureTime = departure,
            StayDurationMinutes = 0
        };

        var countdown = TripStatusTransitionSupport.ResolveDwellCountdown(
            trip,
            tripStop,
            departure.AddMinutes(-17));

        countdown.ShouldNotBeNull();
        countdown.EndsAt.ShouldBe(departure);
        countdown.RemainingSeconds.ShouldBe(17 * 60);
        countdown.RemainingMinutes.ShouldBe(17);
        countdown.IsOverdue.ShouldBeFalse();
    }

    [Test]
    public void DwellCountdownUsesActualArrivalPlusStayWhenLaterThanSchedule()
    {
        var plannedDeparture = new DateTimeOffset(2030, 1, 1, 8, 5, 0, TimeSpan.Zero);
        var actualArrival = new DateTimeOffset(2030, 1, 1, 8, 1, 0, TimeSpan.Zero);
        var trip = new Trip { DepartureTime = plannedDeparture.AddMinutes(-5) };
        var tripStop = new TripStop
        {
            StationId = Guid.NewGuid(),
            StopOrder = 2,
            StopStatus = TripStopStatuses.Arrived,
            ActualArrivalTime = actualArrival,
            PlannedDepartureTime = plannedDeparture,
            StayDurationMinutes = 10
        };

        var countdown = TripStatusTransitionSupport.ResolveDwellCountdown(
            trip,
            tripStop,
            actualArrival.AddMinutes(3));

        countdown.ShouldNotBeNull();
        countdown.EndsAt.ShouldBe(actualArrival.AddMinutes(10));
        countdown.StayDurationMinutes.ShouldBe(10);
        countdown.RemainingSeconds.ShouldBe(7 * 60);
        countdown.RemainingMinutes.ShouldBe(7);
        countdown.IsOverdue.ShouldBeFalse();
    }

    [TestCase(TripStatus.Completed)]
    [TestCase(TripStatus.Cancelled)]
    public void TerminalTripDoesNotReturnDwellCountdown(TripStatus status)
    {
        var now = new DateTimeOffset(2030, 1, 1, 9, 0, 0, TimeSpan.Zero);
        var trip = new Trip
        {
            DepartureTime = now.AddHours(-1),
            TripStatus = status
        };
        var stop = new TripStop
        {
            StationId = Guid.NewGuid(),
            StopOrder = 2,
            StopStatus = TripStopStatuses.Arrived,
            ActualArrivalTime = now,
            PlannedDepartureTime = now.AddMinutes(5),
            StayDurationMinutes = 5
        };

        TripStatusTransitionSupport.ResolveDwellCountdown(trip, stop, now).ShouldBeNull();
    }
}
