using NUnit.Framework;
using SaigonWaterbus.Application.Bookings;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Application.Fares;
using SaigonWaterbus.Application.Trips;
using SaigonWaterbus.Application.UnitTests.TestInfrastructure;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using SaigonWaterbus.Infrastructure.Data;
using Shouldly;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.UnitTests.Bookings;

/// <summary>
/// Trip Regular bán ghế theo chặng: cùng một ghế bán được nhiều lần trên các chặng không
/// giao nhau, và giá vé tính theo quãng đường (base + đơn giá × km, làm tròn lên 1000).
/// </summary>
public class SegmentBookingAndDistanceFareTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 15, 4, 0, 0, TimeSpan.Zero);

    // Tuyến 3 trạm BB(1) --2.5km--> HB(2) --3.5km--> LT(3).
    private const decimal LegOneKm = 2.5m;
    private const decimal LegTwoKm = 3.5m;

    [Test]
    public async Task SameSeatNonOverlappingSegmentsInOneBookingSucceeds()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userContext = await SeatFlowTestData.SeedCustomerAsync(context);
        var trip = await SeedThreeStopTripAsync(context, "TR-SEG-1", withDistances: true);
        var handler = CreateHandler(context, userContext);

        var result = await handler.Handle(
            new CreateBookingCommand(
                "TR-SEG-1",
                [Adult("A1", "BB", "HB"), Adult("A1", "HB", "LT")],
                null),
            CancellationToken.None);

        result.ItemCount.ShouldBe(2);

        var passengers = context.Set<BookingPassenger>()
            .Where(p => p.BookingId == result.BookingId)
            .OrderBy(p => p.FromStopOrder)
            .ToList();
        passengers.Select(p => (p.FromStopOrder, p.ToStopOrder)).ShouldBe([(1, 2), (2, 3)]);
        passengers.ShouldAllBe(p => p.TripSeatId == trip.TripSeatsBySeatCode["A1"].Id);
        passengers.ShouldAllBe(p => p.FromStationId != null && p.ToStationId != null);
    }

    [Test]
    public async Task SecondBookingReusesSeatOnNonOverlappingSegmentButOverlapIsRejected()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userContext = await SeatFlowTestData.SeedCustomerAsync(context);
        await SeedThreeStopTripAsync(context, "TR-SEG-2", withDistances: true);
        var handler = CreateHandler(context, userContext);

        // Khách 1 đi BB→HB ghế A1.
        await handler.Handle(
            new CreateBookingCommand("TR-SEG-2", [Adult("A1", "BB", "HB")], null),
            CancellationToken.None);

        // Khách 2 lấy đúng ghế A1 đi tiếp HB→LT — hợp lệ vì khách 1 đã xuống ở HB.
        var second = await handler.Handle(
            new CreateBookingCommand("TR-SEG-2", [Adult("A1", "HB", "LT")], null),
            CancellationToken.None);
        second.BookingId.ShouldNotBe(Guid.Empty);

        // Khách 3 muốn A1 đi cả tuyến BB→LT — giao cả 2 vé trên → bị chặn.
        var exception = await Should.ThrowAsync<ValidationException>(() => handler.Handle(
            new CreateBookingCommand("TR-SEG-2", [Adult("A1", "BB", "LT")], null),
            CancellationToken.None));
        exception.Errors.SelectMany(x => x.Value).ShouldContain(m => m.Contains("already booked"));
    }

    [Test]
    public async Task DistanceFarePricesEachItemBySegmentKm()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userContext = await SeatFlowTestData.SeedCustomerAsync(context);
        await SeedThreeStopTripAsync(context, "TR-FARE-1", withDistances: true);
        var handler = CreateHandler(context, userContext);

        var result = await handler.Handle(
            new CreateBookingCommand(
                "TR-FARE-1",
                [Adult("A1", "BB", "HB"), Adult("A2", "BB", "LT")],
                null),
            CancellationToken.None);

        // Policy mặc định: 5000 + 1500đ/km, làm tròn lên 1000.
        // BB→HB: 5000 + 1500×2.5 = 8750 → 9000. BB→LT: 5000 + 1500×6 = 14000.
        var expectedShort = DistanceFareSupport.CalculateFare(FarePolicyDefaults.Dto, LegOneKm);
        var expectedFull = DistanceFareSupport.CalculateFare(FarePolicyDefaults.Dto, LegOneKm + LegTwoKm);
        expectedShort.ShouldBe(9000m);
        expectedFull.ShouldBe(14000m);
        result.SubtotalAmount.ShouldBe(expectedShort + expectedFull);

        var prices = context.Set<BookingPassenger>()
            .Where(p => p.BookingId == result.BookingId)
            .Select(p => p.UnitPrice)
            .ToList();
        prices.ShouldBe([9000m, 14000m], ignoreOrder: true);
    }

    [Test]
    public async Task MissingDistanceFallsBackToSeatTypeFare()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userContext = await SeatFlowTestData.SeedCustomerAsync(context);
        await SeedThreeStopTripAsync(context, "TR-FARE-2", withDistances: false);
        var handler = CreateHandler(context, userContext);

        var result = await handler.Handle(
            new CreateBookingCommand("TR-FARE-2", [Adult("A1", "BB", "LT")], null),
            CancellationToken.None);

        // Tuyến chưa nhập km → dùng giá theo loại ghế (FixedFareCalculator = 10000).
        result.SubtotalAmount.ShouldBe(10000m);
    }

    [Test]
    public async Task SeatMapReportsSeatBySegment()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userContext = await SeatFlowTestData.SeedCustomerAsync(context);
        var trip = await SeedThreeStopTripAsync(context, "TR-MAP-1", withDistances: true);
        var bookingHandler = CreateHandler(context, userContext);

        await bookingHandler.Handle(
            new CreateBookingCommand("TR-MAP-1", [Adult("A1", "BB", "HB")], null),
            CancellationToken.None);

        var seatMapHandler = new GetTripSeatMapQueryHandler(
            context, userContext, new FixedTimeProvider(Now));

        // Xem chặng HB→LT: khách trước đã xuống → A1 trống, giá = km chặng sau (5000+1500×3.5=10250→11000).
        var tailView = await seatMapHandler.Handle(
            new GetTripSeatMapQuery(trip.Trip.Id, "HB", "LT"), CancellationToken.None);
        tailView.Seats.Single(s => s.SeatNumber == "A1").Status.ShouldBe("Available");
        tailView.Seats.Single(s => s.SeatNumber == "A1").BasePrice.ShouldBe(11000m);
        tailView.SegmentDistanceKm.ShouldBe(LegTwoKm);

        // Xem chặng giao (BB→HB) và cả tuyến: A1 bận.
        var overlapView = await seatMapHandler.Handle(
            new GetTripSeatMapQuery(trip.Trip.Id, "BB", "HB"), CancellationToken.None);
        overlapView.Seats.Single(s => s.SeatNumber == "A1").Status.ShouldBe("Booked");

        var fullView = await seatMapHandler.Handle(
            new GetTripSeatMapQuery(trip.Trip.Id), CancellationToken.None);
        fullView.Seats.Single(s => s.SeatNumber == "A1").Status.ShouldBe("Booked");
    }

    [Test]
    public async Task SearchTripsCountsAvailabilityAndPriceBySearchedSegment()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userContext = await SeatFlowTestData.SeedCustomerAsync(context);
        await SeedThreeStopTripAsync(context, "TR-SRCH-1", withDistances: true);
        var bookingHandler = CreateHandler(context, userContext);

        await bookingHandler.Handle(
            new CreateBookingCommand("TR-SRCH-1", [Adult("A1", "BB", "HB")], null),
            CancellationToken.None);

        var bb = context.Set<Station>().Single(s => s.StationCode == "BB");
        var hb = context.Set<Station>().Single(s => s.StationCode == "HB");
        var lt = context.Set<Station>().Single(s => s.StationCode == "LT");
        var searchHandler = new SearchTripsQueryHandler(context, new FixedTimeProvider(Now));
        var date = DateOnly.FromDateTime(Now.UtcDateTime);

        // Chặng HB→LT: khách trước đã xuống ở HB → cả 2 ghế trống; giá theo km chặng sau (11000).
        var tailResults = await searchHandler.Handle(
            new SearchTripsQuery(hb.Id, lt.Id, date), CancellationToken.None);
        var tailTrip = tailResults.Single(x => x.TripCode == "TR-SRCH-1");
        tailTrip.AvailableSeats.ShouldBe(2);
        tailTrip.MinPrice.ShouldBe(11000m);

        // Chặng BB→HB (giao vé đã bán): còn 1 ghế; giá theo km chặng đầu (9000).
        var headResults = await searchHandler.Handle(
            new SearchTripsQuery(bb.Id, hb.Id, date), CancellationToken.None);
        var headTrip = headResults.Single(x => x.TripCode == "TR-SRCH-1");
        headTrip.AvailableSeats.ShouldBe(1);
        headTrip.MinPrice.ShouldBe(9000m);
    }

    [Test]
    public void SegmentsOverlapUsesHalfOpenIntervals()
    {
        // Chạm biên (xuống trạm 2, người khác lên trạm 2) → không giao.
        BookingSeatOccupancySupport.SegmentsOverlap(1, 2, 2, 3).ShouldBeFalse();
        BookingSeatOccupancySupport.SegmentsOverlap(2, 3, 1, 2).ShouldBeFalse();
        BookingSeatOccupancySupport.SegmentsOverlap(1, 3, 2, 4).ShouldBeTrue();
        BookingSeatOccupancySupport.SegmentsOverlap(1, 4, 2, 3).ShouldBeTrue();
        BookingSeatOccupancySupport.SegmentsOverlap(int.MinValue, int.MaxValue, 2, 3).ShouldBeTrue();
    }

    [Test]
    public void CalculateFareRoundsUpAndAppliesMinFare()
    {
        var policy = new FarePolicyDto(null, 5000m, 1500m, 1000m, null, "VND");
        DistanceFareSupport.CalculateFare(policy, 2.5m).ShouldBe(9000m);
        DistanceFareSupport.CalculateFare(policy, 6m).ShouldBe(14000m);

        var withMin = policy with { MinFare = 12000m };
        DistanceFareSupport.CalculateFare(withMin, 1m).ShouldBe(12000m);
    }

    [Test]
    public void TryComputeSegmentDistanceKmReturnsNullWhenKmMissing()
    {
        var stops = new List<RouteStop>
        {
            new() { StopOrder = 1 },
            new() { StopOrder = 2, DistanceFromPreviousKm = 2.5m },
            new() { StopOrder = 3 } // admin chưa nhập km chặng 2→3
        };

        DistanceFareSupport.TryComputeSegmentDistanceKm(stops, 1, 2).ShouldBe(2.5m);
        DistanceFareSupport.TryComputeSegmentDistanceKm(stops, 1, 3).ShouldBeNull();
        DistanceFareSupport.TryComputeSegmentDistanceKm(stops, 2, 3).ShouldBeNull();
        DistanceFareSupport.TryComputeSegmentDistanceKm(stops, 2, 2).ShouldBeNull();
    }

    private static CreateBookingCommandHandler CreateHandler(
        ApplicationDbContext context,
        TestUserContext userContext) =>
        new(
            context,
            userContext,
            new SequentialBookingCodeGenerator(),
            new FixedFareCalculator(10000m),
            new FixedTimeProvider(Now));

    private static BookingItemRequest Adult(string seat, string from, string to) =>
        new(seat, "ADULT", from, to, "Nguyen Van A", null, null, null, null, null);

    private sealed record SeededTrip(
        Trip Trip,
        IReadOnlyDictionary<string, TripSeat> TripSeatsBySeatCode);

    /// <summary>Trip Regular trên tuyến 3 trạm BB → HB → LT, tàu 2 ghế STANDARD (A1, A2).</summary>
    private static async Task<SeededTrip> SeedThreeStopTripAsync(
        ApplicationDbContext context,
        string tripCode,
        bool withDistances)
    {
        var bb = await GetOrCreateStationAsync(context, "BB");
        var hb = await GetOrCreateStationAsync(context, "HB");
        var lt = await GetOrCreateStationAsync(context, "LT");

        var route = new Route
        {
            RouteCode = $"R-{tripCode}",
            RouteName = "BB - LT",
            RouteType = RouteTypes.Regular,
            IsBookable = true
        };
        route.RouteStops.Add(new RouteStop { Route = route, Station = bb, StationId = bb.Id, StopOrder = 1 });
        route.RouteStops.Add(new RouteStop
        {
            Route = route, Station = hb, StationId = hb.Id, StopOrder = 2,
            DistanceFromPreviousKm = withDistances ? LegOneKm : null
        });
        route.RouteStops.Add(new RouteStop
        {
            Route = route, Station = lt, StationId = lt.Id, StopOrder = 3,
            DistanceFromPreviousKm = withDistances ? LegTwoKm : null
        });

        var boat = SeatFlowTestData.Boat(SeatSetupType.FullStandard, seatsConfigured: true, BoatStatus.Active);
        boat.SeatCount = 2;
        var seatA1 = new Seat { Boat = boat, BoatId = boat.Id, Code = "A1", Deck = 1, Row = "A", Column = 1 };
        var seatA2 = new Seat { Boat = boat, BoatId = boat.Id, Code = "A2", Deck = 1, Row = "A", Column = 2 };

        var trip = new Trip
        {
            Route = route,
            RouteId = route.Id,
            Boat = boat,
            BoatId = boat.Id,
            TripCode = tripCode,
            TripType = TripTypes.Regular,
            OperatingDate = DateOnly.FromDateTime(Now.UtcDateTime),
            DepartureTime = Now.AddHours(2),
            ArrivalTime = Now.AddHours(3),
            CapacitySnapshot = 2,
            TripStatus = TripStatus.Scheduled
        };

        var tripSeatA1 = new TripSeat { Trip = trip, TripId = trip.Id, Seat = seatA1, SeatId = seatA1.Id, Price = 10000m };
        var tripSeatA2 = new TripSeat { Trip = trip, TripId = trip.Id, Seat = seatA2, SeatId = seatA2.Id, Price = 10000m };

        context.AddRange(route, boat, seatA1, seatA2, trip, tripSeatA1, tripSeatA2);
        await context.SaveChangesAsync();

        return new SeededTrip(trip, new Dictionary<string, TripSeat>
        {
            ["A1"] = tripSeatA1,
            ["A2"] = tripSeatA2
        });
    }

    private static async Task<Station> GetOrCreateStationAsync(ApplicationDbContext context, string stationCode)
    {
        var existing = context.Set<Station>().SingleOrDefault(s => s.StationCode == stationCode);
        if (existing is not null)
        {
            return existing;
        }

        var station = new Station { StationCode = stationCode, StationName = $"Station {stationCode}" };
        context.Add(station);
        await context.SaveChangesAsync();
        return station;
    }

    private sealed class SequentialBookingCodeGenerator : IBookingCodeGenerator
    {
        private int _next;

        public Task<string> GenerateAsync(CancellationToken cancellationToken) =>
            Task.FromResult($"BK-SEG-{Interlocked.Increment(ref _next):D4}");
    }

    private sealed class FixedFareCalculator(decimal fare) : IFareCalculator
    {
        public Task<decimal> CalculateAsync(
            Guid seatId,
            string ticketTypeCode,
            CancellationToken cancellationToken,
            Guid? tripId = null) =>
            Task.FromResult(fare);
    }
}
