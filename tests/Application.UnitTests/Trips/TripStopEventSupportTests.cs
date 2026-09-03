using NUnit.Framework;
using SaigonWaterbus.Application.Trips;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;
using Shouldly;

namespace SaigonWaterbus.Application.UnitTests.Trips;

public class TripStopEventSupportTests
{
    [Test]
    public void LegacyStationRequestSelectsFirstOccurrenceFromUnorderedCollection()
    {
        var repeatedStationId = Guid.NewGuid();
        var stops = CreateRepeatedRoute(repeatedStationId);

        var result = TripStopEventSupport.ResolveTarget(
            [stops[2], stops[1], stops[0]],
            repeatedStationId,
            TripStopStatuses.Arrived);

        result.IsSuccess.ShouldBeTrue();
        result.Stop.ShouldBeSameAs(stops[0]);
    }

    [Test]
    public void LegacyStationRequestSelectsLaterOccurrenceAfterEarlierStopsClose()
    {
        var repeatedStationId = Guid.NewGuid();
        var stops = CreateRepeatedRoute(repeatedStationId);
        Close(stops[0]);
        Close(stops[1]);

        var result = TripStopEventSupport.ResolveTarget(
            stops,
            repeatedStationId,
            TripStopStatuses.Arrived);

        result.IsSuccess.ShouldBeTrue();
        result.Stop.ShouldBeSameAs(stops[2]);
    }

    [Test]
    public void CannotUpdateLaterStopBeforeEveryPriorStopCloses()
    {
        var repeatedStationId = Guid.NewGuid();
        var stops = CreateRepeatedRoute(repeatedStationId);
        Close(stops[0]);
        stops[1].StopStatus = TripStopStatuses.Arrived;

        var result = TripStopEventSupport.ResolveTarget(
            stops,
            stops[2].StationId,
            TripStopStatuses.Arrived,
            tripStopId: stops[2].Id);

        result.IsSuccess.ShouldBeFalse();
        result.Error.ShouldNotBeNull().ShouldContain("các bến trước");
    }

    [Test]
    public void ExactTripStopIdAndStopOrderSelectTheRequestedOccurrence()
    {
        var repeatedStationId = Guid.NewGuid();
        var stops = CreateRepeatedRoute(repeatedStationId);
        Close(stops[0]);
        Close(stops[1]);

        var byId = TripStopEventSupport.ResolveTarget(
            stops,
            repeatedStationId,
            TripStopStatuses.Arriving,
            tripStopId: stops[2].Id);
        var byOrder = TripStopEventSupport.ResolveTarget(
            stops,
            repeatedStationId,
            TripStopStatuses.Arriving,
            stopOrder: 3);

        byId.Stop.ShouldBeSameAs(stops[2]);
        byOrder.Stop.ShouldBeSameAs(stops[2]);
    }

    [Test]
    public void ExactIdentifierMustMatchStationOccurrence()
    {
        var repeatedStationId = Guid.NewGuid();
        var stops = CreateRepeatedRoute(repeatedStationId);

        var byId = TripStopEventSupport.ResolveTarget(
            stops,
            repeatedStationId,
            TripStopStatuses.Arriving,
            tripStopId: stops[1].Id);
        var byOrder = TripStopEventSupport.ResolveTarget(
            stops,
            repeatedStationId,
            TripStopStatuses.Arriving,
            stopOrder: 2);

        byId.IsSuccess.ShouldBeFalse();
        byOrder.IsSuccess.ShouldBeFalse();
    }

    [Test]
    public void TripStopIdAndStopOrderMustIdentifyTheSameOccurrence()
    {
        var repeatedStationId = Guid.NewGuid();
        var stops = CreateRepeatedRoute(repeatedStationId);

        var result = TripStopEventSupport.ResolveTarget(
            stops,
            repeatedStationId,
            TripStopStatuses.Arriving,
            tripStopId: stops[0].Id,
            stopOrder: stops[2].StopOrder);

        result.IsSuccess.ShouldBeFalse();
    }

    [Test]
    public void LaterScheduledStopCannotDepartWithoutAnArrivalEvent()
    {
        var stops = CreateRepeatedRoute(Guid.NewGuid());
        Close(stops[0]);

        var result = TripStopEventSupport.ResolveTarget(
            stops,
            stops[1].StationId,
            TripStopStatuses.Departed,
            tripStopId: stops[1].Id);

        result.IsSuccess.ShouldBeFalse();
    }

    [Test]
    public void RepeatedDepartedEventDoesNotOverwriteActualTimestamps()
    {
        var stop = Stop(1, Guid.NewGuid());
        var arrivedAt = new DateTimeOffset(2030, 1, 1, 8, 0, 0, TimeSpan.Zero);
        var departedAt = arrivedAt.AddMinutes(5);
        stop.StopStatus = TripStopStatuses.Departed;
        stop.ActualArrivalTime = arrivedAt;
        stop.ActualDepartureTime = departedAt;

        TripStopEventSupport.ApplyEvent(
            stop,
            TripStopStatuses.Departed,
            departedAt.AddMinutes(3),
            note: null);

        stop.ActualArrivalTime.ShouldBe(arrivedAt);
        stop.ActualDepartureTime.ShouldBe(departedAt);
    }

    [Test]
    public void ArrivedStopCannotRegressToArriving()
    {
        var stop = Stop(1, Guid.NewGuid());
        stop.StopStatus = TripStopStatuses.Arrived;

        var result = TripStopEventSupport.ResolveTarget(
            [stop],
            stop.StationId,
            TripStopStatuses.Arriving,
            tripStopId: stop.Id);

        result.IsSuccess.ShouldBeFalse();
    }

    private static TripStop[] CreateRepeatedRoute(Guid repeatedStationId) =>
    [
        Stop(1, repeatedStationId),
        Stop(2, Guid.NewGuid()),
        Stop(3, repeatedStationId)
    ];

    private static TripStop Stop(int order, Guid stationId) => new()
    {
        Id = Guid.NewGuid(),
        StopOrder = order,
        StationId = stationId,
        StopStatus = TripStopStatuses.Scheduled
    };

    private static void Close(TripStop stop)
    {
        stop.StopStatus = TripStopStatuses.Departed;
        stop.ActualDepartureTime = new DateTimeOffset(2030, 1, 1, 8, stop.StopOrder, 0, TimeSpan.Zero);
    }
}
