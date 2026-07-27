using NUnit.Framework;
using SaigonWaterbus.Application.Bookings;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Application.TicketTypes;
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
/// Tuyến ngắm cảnh chỉ có bến đầu và bến cuối, và hai bến đó trùng nhau — nên bán ghế nguyên
/// chuyến: khách không chọn chặng, BE tự lấy bến đầu → bến cuối. Cùng một station xuất hiện ở
/// hai stop order nên không được index route stop theo station code.
/// Tuyến waterbus Regular cũng được phép trùng bến đầu/cuối (đi vòng về chỗ cũ) và vẫn bán
/// theo chặng như thường — xem <see cref="RegularRouteThatLoopsBackToOriginStillSellsBySegment"/>.
/// </summary>
public class SightseeingLoopBookingTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 18, 4, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task BookingWithoutStationCodesSucceedsAndCoversWholeTrip()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userContext = await SeatFlowTestData.SeedCustomerAsync(context);
        var seeded = await SeedLoopTripAsync(context, "TR-SIG-1");

        var result = await CreateHandler(context, userContext).Handle(
            new CreateBookingCommand("TR-SIG-1", [Adult("A1"), Adult("A2")], null),
            CancellationToken.None);

        result.ItemCount.ShouldBe(2);

        var passengers = context.Set<BookingPassenger>()
            .Where(p => p.BookingId == result.BookingId)
            .ToList();
        // Đi nguyên chuyến → không lưu stop order (= chiếm ghế cả trip khi check occupancy).
        passengers.ShouldAllBe(p => p.FromStopOrder == null && p.ToStopOrder == null);
        // Vòng lặp: lên và xuống cùng một bến.
        passengers.ShouldAllBe(p => p.FromStationId == seeded.TerminalStationId
                                 && p.ToStationId == seeded.TerminalStationId);
    }

    [Test]
    public async Task StationCodesSentByClientAreIgnoredOnLoopTrip()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userContext = await SeatFlowTestData.SeedCustomerAsync(context);
        await SeedLoopTripAsync(context, "TR-SIG-2");

        // FE cũ gửi bến đầu = bến cuối (trước đây bị validator chặn) → vẫn đặt được.
        var result = await CreateHandler(context, userContext).Handle(
            new CreateBookingCommand(
                "TR-SIG-2",
                [Adult("A1") with { FromStationCode = "BB", ToStationCode = "BB" }],
                null),
            CancellationToken.None);

        result.ItemCount.ShouldBe(1);
    }

    [Test]
    public async Task SightseeingConcessionTicketsUseSharedDefaultPriceModifier()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userContext = await SeatFlowTestData.SeedCustomerAsync(context);
        var seeded = await SeedLoopTripAsync(context, "TR-SIG-DISCOUNT");
        var seats = context.Set<Seat>().Where(x => x.BoatId == seeded.Trip.BoatId).ToList();
        foreach (var seat in seats)
        {
            seat.SeatTypeCode = "CABIN";
        }
        await context.SaveChangesAsync();

        var handler = new CreateBookingCommandHandler(
            context,
            userContext,
            new SequentialBookingCodeGenerator(),
            new FareCalculator(context),
            new FixedTimeProvider(Now));

        var result = await handler.Handle(
            new CreateBookingCommand(
                "TR-SIG-DISCOUNT",
                [
                    Adult("A1") with { TicketTypeCode = "CHILD" },
                    Adult("A2") with { TicketTypeCode = "DISABLED" }
                ],
                null),
            CancellationToken.None);

        result.SubtotalAmount.ShouldBe(10_000m);
        result.TotalAmount.ShouldBe(10_000m);

        var passengers = context.Set<BookingPassenger>()
            .Where(x => x.BookingId == result.BookingId)
            .OrderBy(x => x.PassengerType)
            .ToList();
        passengers.Select(x => x.PassengerType).ShouldBe(["CHILD", "DISABLED"]);
        passengers.Single(x => x.PassengerType == "CHILD").UnitPrice.ShouldBe(5_000m);
        passengers.Single(x => x.PassengerType == "DISABLED").UnitPrice.ShouldBe(5_000m);

        var seniorPrice = await new FareCalculator(context).CalculateAsync(
            seats.Single(x => x.Code == "A1").Id,
            "SENIOR",
            CancellationToken.None,
            seeded.Trip.Id);
        seniorPrice.ShouldBe(5_000m);
    }

    [Test]
    public async Task SightseeingConcessionFareRuleAppliesSameDiscountToWholeGroup()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userContext = await SeatFlowTestData.SeedCustomerAsync(context);
        var seeded = await SeedLoopTripAsync(context, "TR-SIG-RULE");
        var seats = context.Set<Seat>().Where(x => x.BoatId == seeded.Trip.BoatId).ToList();
        foreach (var seat in seats)
        {
            seat.SeatTypeCode = "CABIN";
        }
        await context.SaveChangesAsync();
        await new UpdateSightseeingConcessionFareRuleCommandHandler(context).Handle(
            new UpdateSightseeingConcessionFareRuleCommand(75m),
            CancellationToken.None);

        var handler = new CreateBookingCommandHandler(
            context,
            userContext,
            new SequentialBookingCodeGenerator(),
            new FareCalculator(context),
            new FixedTimeProvider(Now));

        var result = await handler.Handle(
            new CreateBookingCommand(
                "TR-SIG-RULE",
                [
                    Adult("A1") with { TicketTypeCode = "CHILD" },
                    Adult("A2") with { TicketTypeCode = "DISABLED" }
                ],
                null),
            CancellationToken.None);

        result.SubtotalAmount.ShouldBe(5_000m);
        result.TotalAmount.ShouldBe(5_000m);

        var seniorPrice = await new FareCalculator(context).CalculateAsync(
            seats.Single(x => x.Code == "A1").Id,
            "SENIOR",
            CancellationToken.None,
            seeded.Trip.Id);
        seniorPrice.ShouldBe(2_500m);

        context.Set<TicketFareRule>()
            .Where(x => x.RouteType == RouteTypes.SightseeingLoop)
            .OrderBy(x => x.TicketTypeCode)
            .Select(x => new ValueTuple<string, decimal>(x.TicketTypeCode, x.PriceModifier))
            .ToList()
            .ShouldBe([
                new ValueTuple<string, decimal>("CHILD", 0.25m),
                new ValueTuple<string, decimal>("DISABLED", 0.25m),
                new ValueTuple<string, decimal>("SENIOR", 0.25m)
            ]);
    }

    [Test]
    public async Task SeatBookedOnLoopTripCannotBeBookedAgain()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userContext = await SeatFlowTestData.SeedCustomerAsync(context);
        await SeedLoopTripAsync(context, "TR-SIG-3");
        var handler = CreateHandler(context, userContext);

        await handler.Handle(
            new CreateBookingCommand("TR-SIG-3", [Adult("A1")], null), CancellationToken.None);

        var exception = await Should.ThrowAsync<ValidationException>(() => handler.Handle(
            new CreateBookingCommand("TR-SIG-3", [Adult("A1")], null), CancellationToken.None));
        exception.Errors.SelectMany(x => x.Value).ShouldContain(m => m.Contains("already booked"));
    }

    [Test]
    public async Task MissingStationCodesOnSegmentTripIsRejected()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userContext = await SeatFlowTestData.SeedCustomerAsync(context);
        await SeedLoopTripAsync(context, "TR-SIG-4", routeType: RouteTypes.Regular);

        // Chuyến bán theo chặng vẫn bắt buộc trạm lên/xuống — rule chuyển từ validator xuống handler.
        var exception = await Should.ThrowAsync<ValidationException>(() =>
            CreateHandler(context, userContext).Handle(
                new CreateBookingCommand("TR-SIG-4", [Adult("A1")], null), CancellationToken.None));
        exception.Errors.SelectMany(x => x.Value).ShouldContain(m => m.Contains("bắt buộc"));
    }

    [Test]
    public async Task BookingListAndDetailReportSightseeingServiceType()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userContext = await SeatFlowTestData.SeedCustomerAsync(context);
        await SeedLoopTripAsync(context, "TR-SIG-6");
        await SeedLoopTripAsync(context, "TR-SIG-7", routeType: RouteTypes.Regular);
        var handler = CreateHandler(context, userContext);

        var loopBooking = await handler.Handle(
            new CreateBookingCommand("TR-SIG-6", [Adult("A1")], null), CancellationToken.None);
        await handler.Handle(
            new CreateBookingCommand("TR-SIG-7", [Adult("A1") with { FromStationCode = "BB", ToStationCode = "LT" }], null),
            CancellationToken.None);
        var loopBookingEntity = context.Set<Booking>().Single(x => x.Id == loopBooking.BookingId);
        loopBookingEntity.PointsUsed = 120;
        loopBookingEntity.PointsEarned = 75;
        loopBookingEntity.InsuranceSnapshot = InsuranceSnapshot();
        await context.SaveChangesAsync();

        var list = await new GetBookingListQueryHandler(context, userContext)
            .Handle(new GetBookingListQuery(), CancellationToken.None);
        var loopListItem = list.Single(x => x.BookingId == loopBooking.BookingId);
        loopListItem.ServiceType.ShouldBe(BookingServiceTypes.Sightseeing);
        loopListItem.PointsUsed.ShouldBe(120);
        loopListItem.PointsEarned.ShouldBe(75);
        loopListItem.Insurance.ShouldNotBeNull();
        loopListItem.Insurance.Code.ShouldBe("INS-SEAT");
        list.Single(x => x.BookingId != loopBooking.BookingId).ServiceType
            .ShouldBe(BookingServiceTypes.Waterbus);

        var detail = await new GetBookingDetailQueryHandler(context, userContext)
            .Handle(new GetBookingDetailQuery(loopBooking.BookingId), CancellationToken.None);
        detail.ServiceType.ShouldBe(BookingServiceTypes.Sightseeing);
        detail.RouteType.ShouldBe(RouteTypes.SightseeingLoop);
        detail.PointsUsed.ShouldBe(120);
        detail.PointsEarned.ShouldBe(75);
        detail.Insurance.ShouldNotBeNull();
        detail.Insurance.Code.ShouldBe("INS-SEAT");
    }

    [Test]
    public async Task BookingHistoryReportsSightseeingServiceType()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userContext = await SeatFlowTestData.SeedCustomerAsync(context);
        await SeedLoopTripAsync(context, "TR-SIG-8");

        var created = await CreateHandler(context, userContext).Handle(
            new CreateBookingCommand("TR-SIG-8", [Adult("A1")], null), CancellationToken.None);
        var booking = context.Set<Booking>().Single(x => x.Id == created.BookingId);
        booking.PointsUsed = 60;
        booking.PointsEarned = 30;
        booking.InsuranceSnapshot = InsuranceSnapshot();
        await context.SaveChangesAsync();

        var items = await new Application.BookingHistory.GetMyBookingHistoryQueryHandler(context, userContext)
            .Handle(new Application.BookingHistory.GetMyBookingHistoryQuery(), CancellationToken.None);

        var item = items.ShouldHaveSingleItem();
        item.Type.ShouldBe("StandardBooking");
        item.ServiceType.ShouldBe(BookingServiceTypes.Sightseeing);
        item.PointsUsed.ShouldBe(60);
        item.PointsEarned.ShouldBe(30);
        item.Insurance.ShouldNotBeNull();
        item.Insurance.Code.ShouldBe("INS-SEAT");
    }

    [Test]
    public async Task RegularRouteThatLoopsBackToOriginStillSellsBySegment()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userContext = await SeatFlowTestData.SeedCustomerAsync(context);
        // Tuyến waterbus thường nhưng đi vòng về bến đầu: BB(1) → HB(2) → BB(3).
        await SeedLoopTripAsync(context, "TR-LOOP-REG", routeType: RouteTypes.Regular, loopBackOnRegular: true);
        var handler = CreateHandler(context, userContext);

        // Chặng lượt đi BB→HB = (1,2); chặng lượt về HB→BB phải resolve về lần ghé BB MUỘN NHẤT (2,3),
        // không phải stop 1 — trước đây chỗ này ném duplicate key hoặc "must come before".
        var outbound = await handler.Handle(
            new CreateBookingCommand(
                "TR-LOOP-REG",
                [Adult("A1") with { FromStationCode = "BB", ToStationCode = "HB" }], null),
            CancellationToken.None);
        var inbound = await handler.Handle(
            new CreateBookingCommand(
                "TR-LOOP-REG",
                [Adult("A1") with { FromStationCode = "HB", ToStationCode = "BB" }], null),
            CancellationToken.None);

        var stopOrders = context.Set<BookingPassenger>()
            .Where(p => p.BookingId == outbound.BookingId || p.BookingId == inbound.BookingId)
            .OrderBy(p => p.FromStopOrder)
            .Select(p => new { p.FromStopOrder, p.ToStopOrder })
            .ToList();
        stopOrders.Select(x => (x.FromStopOrder, x.ToStopOrder)).ShouldBe([(1, 2), (2, 3)]);
    }

    [Test]
    public void RepeatedDestinationResolvesToTheFirstArrivalNotTheLast()
    {
        // Tuyến ghé BB hai lần giữa đường: BB(1) → HB(2) → BB(3) → HB(4).
        var bb = new Station { StationCode = "BB", StationName = "Station BB" };
        var hb = new Station { StationCode = "HB", StationName = "Station HB" };
        var stops = new List<RouteStop>
        {
            new() { StopOrder = 1, Station = bb },
            new() { StopOrder = 2, Station = hb },
            new() { StopOrder = 3, Station = bb },
            new() { StopOrder = 4, Station = hb }
        };

        // Khách BB→HB xuống ngay lần đầu tàu tới HB (stop 2), không phải đi vòng tới stop 4.
        var (from, to) = TripSegmentSupport.ResolveStops(stops, "BB", "HB", "From", "To");
        from.StopOrder.ShouldBe(1);
        to.StopOrder.ShouldBe(2);

        // Khách lên ở HB (stop 2) muốn về BB → lần ghé BB kế tiếp là stop 3.
        var (backFrom, backTo) = TripSegmentSupport.ResolveStops(stops, "HB", "BB", "From", "To");
        backFrom.StopOrder.ShouldBe(2);
        backTo.StopOrder.ShouldBe(3);
    }

    [Test]
    public async Task SeatMapTellsFrontendTripIsNotSoldBySegment()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userContext = await SeatFlowTestData.SeedCustomerAsync(context);
        var seeded = await SeedLoopTripAsync(context, "TR-SIG-5");

        var seatMap = await new GetTripSeatMapQueryHandler(context, userContext, new FixedTimeProvider(Now))
            .Handle(new GetTripSeatMapQuery(seeded.Trip.Id), CancellationToken.None);

        seatMap.RouteType.ShouldBe(RouteTypes.SightseeingLoop);
        seatMap.SellsBySegment.ShouldBeFalse();
    }

    private static CreateBookingCommandHandler CreateHandler(
        ApplicationDbContext context,
        TestUserContext userContext) =>
        new(
            context,
            userContext,
            new SequentialBookingCodeGenerator(),
            new FixedFareCalculator(150000m),
            new FixedTimeProvider(Now));

    private static BookingItemRequest Adult(string seat) =>
        new(seat, "ADULT", null, null, "Nguyen Van A", null, null, null, null, null);

    private static BookingInsuranceSnapshot InsuranceSnapshot() =>
        new()
        {
            InsurancePackageId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Code = "INS-SEAT",
            Name = "Seat insurance",
            BookingType = Booking.SeatBookingType,
            IsRequired = false,
            UnitPremiumAmount = 10000m,
            CoverageAmount = 100000000m,
            Currency = "VND",
            Quantity = 1,
            TotalAmount = 10000m,
            Conditions = ["Valid for one passenger"],
            QuotedAt = Now
        };

    private sealed record SeededLoopTrip(Trip Trip, Guid TerminalStationId);

    /// <summary>
    /// Trip để test, hình dạng tuyến theo đúng dữ liệu thật:
    /// <list type="bullet">
    /// <item>SightseeingLoop: ĐÚNG 2 bến, cả hai là một bến — BB(1) → BB(2), không có bến trung gian.</item>
    /// <item>Regular vòng lặp: BB(1) → HB(2) → BB(3) — waterbus thường đi vòng về bến đầu, vẫn bán theo chặng.</item>
    /// <item>Regular thẳng: BB(1) → HB(2) → LT(3).</item>
    /// </list>
    /// Tàu 2 ghế.
    /// </summary>
    private static async Task<SeededLoopTrip> SeedLoopTripAsync(
        ApplicationDbContext context,
        string tripCode,
        string routeType = RouteTypes.SightseeingLoop,
        bool loopBackOnRegular = false)
    {
        var bb = await GetOrCreateStationAsync(context, "BB");

        var route = new Route
        {
            RouteCode = $"R-{tripCode}",
            RouteName = "Ngam canh BB",
            RouteType = routeType,
            IsBookable = true
        };
        route.RouteStops.Add(new RouteStop { Route = route, Station = bb, StationId = bb.Id, StopOrder = 1 });

        if (routeType == RouteTypes.SightseeingLoop)
        {
            // Tuyến ngắm cảnh chỉ có bến đầu và bến cuối, và hai bến đó trùng nhau.
            route.RouteStops.Add(new RouteStop
            {
                Route = route, Station = bb, StationId = bb.Id, StopOrder = 2, DistanceFromPreviousKm = 8m
            });
        }
        else
        {
            var hb = await GetOrCreateStationAsync(context, "HB");
            // Tuyến bán theo chặng: bến cuối trùng bến đầu (vòng lặp) hoặc khác bến đầu (tuyến thẳng).
            var lastStation = loopBackOnRegular ? bb : await GetOrCreateStationAsync(context, "LT");
            route.RouteStops.Add(new RouteStop
            {
                Route = route, Station = hb, StationId = hb.Id, StopOrder = 2, DistanceFromPreviousKm = 4m
            });
            route.RouteStops.Add(new RouteStop
            {
                Route = route, Station = lastStation, StationId = lastStation.Id, StopOrder = 3,
                DistanceFromPreviousKm = 4m
            });
        }

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

        context.AddRange(
            route, boat, seatA1, seatA2, trip,
            new TripSeat { Trip = trip, TripId = trip.Id, Seat = seatA1, SeatId = seatA1.Id, Price = 150000m },
            new TripSeat { Trip = trip, TripId = trip.Id, Seat = seatA2, SeatId = seatA2.Id, Price = 150000m });
        await context.SaveChangesAsync();

        return new SeededLoopTrip(trip, bb.Id);
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
            Task.FromResult($"BK-SIG-{Interlocked.Increment(ref _next):D4}");
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
