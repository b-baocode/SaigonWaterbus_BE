using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using SaigonWaterbus.Application.Operations;
using SaigonWaterbus.Application.UnitTests.TestInfrastructure;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using Shouldly;

namespace SaigonWaterbus.Application.UnitTests.Operations;

public class OperationScheduleSynchronizerTests
{
    private static readonly DateTimeOffset FixedNow =
        new(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task SyncIsIdempotentForRegularTrips()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var trip = Trip("TR-001", TripStatus.Scheduled);
        context.Add(trip.Route);
        context.Add(trip);
        await context.SaveChangesAsync();

        var synchronizer = CreateSynchronizer(context);
        var count1 = await synchronizer.SyncAsync(FixedNow.AddDays(-1), FixedNow.AddDays(1), CancellationToken.None);
        var count2 = await synchronizer.SyncAsync(FixedNow.AddDays(-1), FixedNow.AddDays(1), CancellationToken.None);

        count1.ShouldBe(1);
        count2.ShouldBe(1);
        context.OperationScheduleEntries.Count(x => x.SourceId == trip.Id).ShouldBe(1);
    }

    [Test]
    public async Task SyncRecalculatesAdjustedTimesWhenDelayedTripTimeChanges()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var trip = Trip("TR-002", TripStatus.Scheduled);
        context.Add(trip.Route);
        context.Add(trip);
        await context.SaveChangesAsync();

        var synchronizer = CreateSynchronizer(context);
        await synchronizer.SyncAsync(FixedNow.AddDays(-1), FixedNow.AddDays(1), CancellationToken.None);

        var entry = context.OperationScheduleEntries.Single(x => x.SourceId == trip.Id);
        entry.DelayMinutes = 20;
        entry.DelayReason = "Weather";
        entry.OperationStatus = OperationStatuses.Delayed;
        entry.AdjustedStartAt = entry.StartAt.AddMinutes(entry.DelayMinutes);
        entry.AdjustedEndAt = entry.EndAt.AddMinutes(entry.DelayMinutes);
        await context.SaveChangesAsync();

        trip.DepartureTime = trip.DepartureTime.AddMinutes(15);
        trip.ArrivalTime = trip.ArrivalTime.AddMinutes(15);
        await context.SaveChangesAsync();
        await synchronizer.SyncAsync(FixedNow.AddDays(-1), FixedNow.AddDays(1), CancellationToken.None);

        entry = context.OperationScheduleEntries.Single(x => x.SourceId == trip.Id);
        entry.StartAt.ShouldBe(trip.DepartureTime.ToUniversalTime());
        entry.AdjustedStartAt.ShouldBe(entry.StartAt.AddMinutes(20));
        entry.AdjustedEndAt.ShouldBe(entry.EndAt.AddMinutes(20));
    }

    [Test]
    public async Task SyncAllowsDelayedTripToProgressWhenSourceStarts()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var trip = Trip("TR-003", TripStatus.Scheduled);
        context.Add(trip.Route);
        context.Add(trip);
        await context.SaveChangesAsync();

        var synchronizer = CreateSynchronizer(context);
        await synchronizer.SyncAsync(FixedNow.AddDays(-1), FixedNow.AddDays(1), CancellationToken.None);

        var entry = context.OperationScheduleEntries.Single(x => x.SourceId == trip.Id);
        entry.DelayMinutes = 10;
        entry.OperationStatus = OperationStatuses.Delayed;
        await context.SaveChangesAsync();

        trip.TripStatus = TripStatus.InProgress;
        await context.SaveChangesAsync();
        await synchronizer.SyncAsync(FixedNow.AddDays(-1), FixedNow.AddDays(1), CancellationToken.None);

        context.OperationScheduleEntries.Single(x => x.SourceId == trip.Id)
            .OperationStatus.ShouldBe(OperationStatuses.Departed);
    }

    [TestCase(TripStatus.InProgress, OperationStatuses.Departed)]
    [TestCase(TripStatus.Completed, OperationStatuses.Arrived)]
    public async Task SyncMapsTripStatusToOperationStatus(
        TripStatus tripStatus,
        string expectedOperationStatus)
    {
        await using var context = SeatFlowTestData.CreateContext();
        var trip = Trip($"TR-{(int)tripStatus:000}", tripStatus);
        context.Add(trip.Route);
        context.Add(trip);
        await context.SaveChangesAsync();

        await CreateSynchronizer(context)
            .SyncAsync(FixedNow.AddDays(-1), FixedNow.AddDays(1), CancellationToken.None);

        context.OperationScheduleEntries.Single(x => x.SourceId == trip.Id)
            .OperationStatus.ShouldBe(expectedOperationStatus);
    }

    [Test]
    public async Task SyncIncludesCustomBookingAfterDepositIsPaidEvenBeforeManagerAssignment()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var vessel = SeatFlowTestData.Vessel(
            SeatSetupType.FullStandard,
            seatsConfigured: true,
            status: VesselStatus.Active);
        var request = CustomBooking(vessel, CustomBookingDepositPaymentStatus.Paid);
        context.Add(vessel);
        context.Add(request);
        await context.SaveChangesAsync();

        var count = await CreateSynchronizer(context)
            .SyncAsync(FixedNow.AddDays(-1), FixedNow.AddDays(3), CancellationToken.None);

        count.ShouldBe(1);
        var entry = context.OperationScheduleEntries.Single(x => x.SourceId == request.Id);
        entry.SourceType.ShouldBe(OperationScheduleSourceType.CustomBooking);
        entry.VesselId.ShouldBe(vessel.Id);
        entry.VesselCode.ShouldBe(vessel.Code);
        entry.StartAt.ShouldBe(new DateTimeOffset(
            request.DepartureDate.ToDateTime(request.PreferredStartTime!.Value),
            TimeSpan.FromHours(7)).ToUniversalTime());
        entry.EndAt.ShouldBe(entry.StartAt.AddMinutes(request.EstimatedDurationMinutes));
        entry.ScheduleState.ShouldBe("Reserved");
        entry.PaymentStage.ShouldBe("DepositPaid");
        entry.OperationStatus.ShouldBe(OperationStatuses.Scheduled);
        entry.AssignedManagerUserId.ShouldBeNull();
    }

    [Test]
    public async Task SyncSkipsCustomBookingBeforeDepositIsPaid()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var vessel = SeatFlowTestData.Vessel(
            SeatSetupType.FullStandard,
            seatsConfigured: true,
            status: VesselStatus.Active);
        var request = CustomBooking(vessel, CustomBookingDepositPaymentStatus.Pending);
        context.Add(vessel);
        context.Add(request);
        await context.SaveChangesAsync();

        var count = await CreateSynchronizer(context)
            .SyncAsync(FixedNow.AddDays(-1), FixedNow.AddDays(3), CancellationToken.None);

        count.ShouldBe(0);
        context.OperationScheduleEntries.Count(x => x.SourceId == request.Id).ShouldBe(0);
    }

    private static OperationScheduleSynchronizer CreateSynchronizer(Infrastructure.Data.ApplicationDbContext context) =>
        new(
            context,
            new FixedTimeProvider(FixedNow),
            new TestDatabaseExceptionClassifier(),
            NullLogger<OperationScheduleSynchronizer>.Instance);

    private static Trip Trip(string code, TripStatus status)
    {
        var departureTime = FixedNow.AddHours(8);
        var route = Route(code);
        return new Trip
        {
            Route = route,
            RouteId = route.Id,
            TripCode = code,
            OperatingDate = DateOnly.FromDateTime(departureTime.Date),
            DepartureTime = departureTime,
            ArrivalTime = departureTime.AddMinutes(30),
            CapacitySnapshot = 50,
            TripStatus = status
        };
    }

    private static Route Route(string code)
    {
        var from = Station($"{code}-A", "Station A");
        var to = Station($"{code}-B", "Station B");
        var route = new Route
        {
            RouteCode = $"R-{code}",
            RouteName = $"Route {code}",
            Status = "Active"
        };

        route.RouteStops =
        [
            new RouteStop
            {
                Route = route,
                Station = from,
                StationId = from.Id,
                StopOrder = 1
            },
            new RouteStop
            {
                Route = route,
                Station = to,
                StationId = to.Id,
                StopOrder = 2
            }
        ];

        return route;
    }

    private static Station Station(string code, string name) =>
        new()
        {
            StationCode = code,
            StationName = name,
            Status = StationStatus.Active
        };

    private static CustomBookingRequest CustomBooking(
        Vessel vessel,
        CustomBookingDepositPaymentStatus depositPaymentStatus)
    {
        var departureDate = DateOnly.FromDateTime(FixedNow.AddDays(2).ToOffset(TimeSpan.FromHours(7)).DateTime);
        return new CustomBookingRequest
        {
            ContactName = "Customer",
            ContactPhone = "+84900000000",
            RequestedNumberOfDecks = 1,
            RequestedSeatSetupType = SeatSetupType.FullStandard,
            RentalUnit = VesselRentalUnit.Hour,
            AssignedVesselId = vessel.Id,
            AssignedVessel = vessel,
            DepartureDate = departureDate,
            PreferredStartTime = new TimeOnly(9, 0),
            EstimatedDurationMinutes = 120,
            FromLocation = "Bach Dang",
            ToLocation = "Linh Dong",
            PassengerCount = 10,
            AdultCount = 10,
            ChildCount = 0,
            Status = CustomBookingRequestStatus.Confirmed,
            Quote = new CustomBookingQuote
            {
                QuotedPrice = 5000000m,
                DepositPercent = 50m,
                DepositAmount = 2500000m,
                RemainingAmount = 2500000m,
                DepositPaymentStatus = depositPaymentStatus
            }
        };
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
