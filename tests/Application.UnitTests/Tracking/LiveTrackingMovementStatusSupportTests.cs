using NUnit.Framework;
using SaigonWaterbus.Application.Tracking;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using Shouldly;

namespace SaigonWaterbus.Application.UnitTests.Tracking;

public class LiveTrackingMovementStatusSupportTests
{
    private static readonly DateTimeOffset Now =
        new(2030, 1, 1, 12, 0, 0, TimeSpan.FromHours(7));

    [TestCase(TripStatus.Scheduled)]
    [TestCase(TripStatus.Boarding)]
    [TestCase(TripStatus.Delayed)]
    public void LiveMovingGpsWinsOverScheduleStatus(TripStatus tripStatus)
    {
        var trip = Trip(tripStatus);

        LiveTrackingMovementStatusSupport.Resolve(
            trip, [], "moving", 12m, isGpsOnline: true, Now).ShouldBe("Moving");
    }

    [Test]
    public void ArrivedStopReturnsAtStationEvenWhenGpsReportsMoving()
    {
        var trip = Trip(TripStatus.InProgress);
        trip.TripStops.Add(new TripStop
        {
            StopStatus = TripStopStatuses.Arrived,
            ActualArrivalTime = Now.AddMinutes(-1)
        });

        LiveTrackingMovementStatusSupport.Resolve(
            trip, trip.TripStops.ToArray(), "moving", 10m, true, Now).ShouldBe("AtStation");
    }

    [Test]
    public void DepartedStopReturnsMovingAndCompletedTripStaysCompleted()
    {
        var trip = Trip(TripStatus.InProgress);
        trip.TripStops.Add(new TripStop
        {
            StopStatus = TripStopStatuses.Departed,
            ActualArrivalTime = Now.AddMinutes(-2),
            ActualDepartureTime = Now
        });

        LiveTrackingMovementStatusSupport.Resolve(
            trip, trip.TripStops.ToArray(), "idle", 0m, true, Now).ShouldBe("Moving");

        trip.TripStatus = TripStatus.Completed;
        LiveTrackingMovementStatusSupport.Resolve(
            trip, trip.TripStops.ToArray(), "moving", 10m, true, Now).ShouldBe("Completed");
    }

    private static Trip Trip(TripStatus status) => new()
    {
        TripCode = "TR-LIVE-STATUS",
        DepartureTime = Now.AddMinutes(30),
        ArrivalTime = Now.AddHours(1),
        TripStatus = status
    };
}
