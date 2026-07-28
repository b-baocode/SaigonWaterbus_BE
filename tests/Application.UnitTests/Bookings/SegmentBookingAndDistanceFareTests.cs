using NUnit.Framework;
using SaigonWaterbus.Application.Bookings;
using SaigonWaterbus.Application.Common;
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
    public async Task SeatBookedOnWholeRouteIsLockedForTailSegmentOfAnotherCustomer()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userContext = await SeatFlowTestData.SeedCustomerAsync(context);
        var trip = await SeedThreeStopTripAsync(context, "TR-SEG-3", withDistances: true);
        var handler = CreateHandler(context, userContext);

        // Khách 1 đi cả tuyến BB→LT ghế A1.
        await handler.Handle(
            new CreateBookingCommand("TR-SEG-3", [Adult("A1", "BB", "LT")], null),
            CancellationToken.None);

        // Khách 2 đi HB→LT — giao chặng với khách 1 → phải bị chặn.
        var exception = await Should.ThrowAsync<ValidationException>(() => handler.Handle(
            new CreateBookingCommand("TR-SEG-3", [Adult("A1", "HB", "LT")], null),
            CancellationToken.None));
        exception.Errors.SelectMany(x => x.Value).ShouldContain(m => m.Contains("already booked"));

        // Và sơ đồ ghế của chặng HB→LT cũng phải báo A1 đã bán.
        var seatMapHandler = new GetTripSeatMapQueryHandler(
            context, userContext, new FixedTimeProvider(Now));
        var tailView = await seatMapHandler.Handle(
            new GetTripSeatMapQuery(trip.Trip.Id, "HB", "LT"), CancellationToken.None);
        tailView.Seats.Single(s => s.SeatNumber == "A1").Status.ShouldBe("Booked");
    }

    [Test]
    public async Task CancellingSegmentBookingReleasesOnlyThatSegment()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userContext = await SeatFlowTestData.SeedCustomerAsync(context);
        var trip = await SeedThreeStopTripAsync(context, "TR-SEG-4", withDistances: true);
        var handler = CreateHandler(context, userContext);

        // Ghế A1 có 2 vé trên 2 chặng không giao nhau: BB→HB và HB→LT.
        await handler.Handle(
            new CreateBookingCommand("TR-SEG-4", [Adult("A1", "BB", "HB")], null),
            CancellationToken.None);
        var tail = await handler.Handle(
            new CreateBookingCommand("TR-SEG-4", [Adult("A1", "HB", "LT")], null),
            CancellationToken.None);

        var notifier = new RecordingTripSeatNotifier();
        var cancelHandler = new CancelBookingCommandHandler(
            context, userContext, new FixedTimeProvider(Now), notifier);
        await cancelHandler.Handle(new CancelBookingCommand(tail.BookingId), CancellationToken.None);

        // Chỉ chặng HB→LT được nhả, KHÔNG phải cả trip (null/null) — vé BB→HB vẫn đang giữ ghế.
        var change = notifier.Published.SelectMany(p => p.Changes).Single();
        change.SeatCode.ShouldBe("A1");
        change.Status.ShouldBe("Available");
        change.FromStopOrder.ShouldBe(2);
        change.ToStopOrder.ShouldBe(3);

        // Sơ đồ ghế chặng BB→HB vẫn phải báo A1 đã bán.
        var seatMapHandler = new GetTripSeatMapQueryHandler(
            context, userContext, new FixedTimeProvider(Now));
        var headView = await seatMapHandler.Handle(
            new GetTripSeatMapQuery(trip.Trip.Id, "BB", "HB"), CancellationToken.None);
        headView.Seats.Single(s => s.SeatNumber == "A1").Status.ShouldBe("Booked");
    }

    [Test]
    public void BookingCutoffBeforeBoardingStationDepartureIsTenMinutes()
    {
        BookingExpirationPolicy.BookingCutoffBeforeDeparture.ShouldBe(TimeSpan.FromMinutes(10));
    }

    [Test]
    public async Task BookingHoldExpiresAtIsCappedByBoardingCutoff()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userContext = await SeatFlowTestData.SeedCustomerAsync(context);
        var seeded = await SeedThreeStopTripAsync(context, "TR-HOLD-CUT", withDistances: true);
        var bookingDeadline = BookingCutoffSupport.ResolveBookingDeadline(seeded.Trip, 1, 2);
        var bookingNow = bookingDeadline.AddMinutes(-2);
        var handler = CreateHandler(context, userContext, bookingNow);

        var result = await handler.Handle(
            new CreateBookingCommand("TR-HOLD-CUT", [Adult("A1", "BB", "HB")], null),
            CancellationToken.None);

        result.HoldExpiresAt.ShouldBe(bookingDeadline);

        var booking = context.Set<Booking>().Single(x => x.Id == result.BookingId);
        booking.HoldExpiresAt.ShouldBe(bookingDeadline);
        booking.HoldExpiresAt!.Value.ShouldBeLessThan(
            bookingNow.Add(BookingSeatOccupancySupport.BookingHoldDuration));
    }

    [Test]
    public async Task BookingCutoffFollowsBoardingStationNotFirstStation()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userContext = await SeatFlowTestData.SeedCustomerAsync(context);
        await SeedThreeStopTripAsync(context, "TR-CUT-1", withDistances: true);

        // Chuyến rời BB lúc Now+2h; mặc định 15 phút/chặng → tàu rời HB lúc Now+2h15.
        // Cửa sổ "BB đã đóng nhưng HB còn mở" là [BB−cutoff, HB−cutoff), rộng đúng bằng 15 phút
        // chạy giữa hai bến bất kể cutoff bao nhiêu. Lấy điểm giữa để test không bám vào con số
        // cụ thể của BookingCutoffBeforeDeparture, cũng không rơi vào biên.
        var cutoff = BookingExpirationPolicy.BookingCutoffBeforeDeparture;
        var afterFirstStationCutoff = Now.AddHours(2)
            .Subtract(cutoff)
            .AddMinutes((double)TripStopScheduleSupport.DefaultTravelMinutes / 2);
        var handler = CreateHandler(context, userContext, afterFirstStationCutoff);

        var exception = await Should.ThrowAsync<ValidationException>(() => handler.Handle(
            new CreateBookingCommand("TR-CUT-1", [Adult("A1", "BB", "HB")], null),
            CancellationToken.None));
        exception.Errors.SelectMany(x => x.Value).ShouldContain(m => m.Contains("ngừng bán vé"));

        // Bến lên muộn hơn vẫn bán được — trước đây bị chặn oan vì tính theo bến đầu tuyến.
        var late = await handler.Handle(
            new CreateBookingCommand("TR-CUT-1", [Adult("A1", "HB", "LT")], null),
            CancellationToken.None);
        late.BookingId.ShouldNotBe(Guid.Empty);
    }

    [Test]
    public async Task CancelIsAllowedUntilBoardingStationNotFirstStation()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userContext = await SeatFlowTestData.SeedCustomerAsync(context);
        await SeedThreeStopTripAsync(context, "TR-CUT-2", withDistances: true);

        // Đặt vé chặng HB→LT từ sớm (còn trong hạn bán).
        var booked = await CreateHandler(context, userContext).Handle(
            new CreateBookingCommand("TR-CUT-2", [Adult("A1", "HB", "LT")], null),
            CancellationToken.None);

        // Tàu đã rời BB (bến đầu) nhưng chưa tới HB → khách vẫn phải hủy được.
        var afterFirstStationDeparture = Now.AddHours(2).AddMinutes(1);
        var cancelHandler = new CancelBookingCommandHandler(
            context, userContext, new FixedTimeProvider(afterFirstStationDeparture));
        await cancelHandler.Handle(new CancelBookingCommand(booked.BookingId), CancellationToken.None);

        var cancelled = context.Set<Booking>().Single(b => b.Id == booked.BookingId);
        cancelled.BookingStatus.ShouldBe(BookingStatus.Cancelled);
    }

    [Test]
    public async Task SeatMapFlagsBookingClosedPerBoardingStation()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userContext = await SeatFlowTestData.SeedCustomerAsync(context);
        var trip = await SeedThreeStopTripAsync(context, "TR-CUT-3", withDistances: true);

        // Cùng mốc như test hạn bán: BB đã đóng, HB còn mở.
        var cutoff = BookingExpirationPolicy.BookingCutoffBeforeDeparture;
        var afterFirstStationCutoff = Now.AddHours(2)
            .Subtract(cutoff)
            .AddMinutes((double)TripStopScheduleSupport.DefaultTravelMinutes / 2);
        var handler = new GetTripSeatMapQueryHandler(
            context, userContext, new FixedTimeProvider(afterFirstStationCutoff));

        var closed = await handler.Handle(
            new GetTripSeatMapQuery(trip.Trip.Id, "BB", "HB"), CancellationToken.None);
        closed.IsBookingClosed.ShouldBeTrue();

        var open = await handler.Handle(
            new GetTripSeatMapQuery(trip.Trip.Id, "HB", "LT"), CancellationToken.None);
        open.IsBookingClosed.ShouldBeFalse();
    }

    [Test]
    public async Task BoardingTripStillAllowsLaterOpenBoardingStation()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userContext = await SeatFlowTestData.SeedCustomerAsync(context);
        var seeded = await SeedThreeStopTripAsync(context, "TR-BOARD-1", withDistances: true);
        seeded.Trip.TripStatus = TripStatus.Boarding;
        await SeedTripStopsAsync(context, seeded.Trip);
        await context.SaveChangesAsync();

        var hb = context.Set<Station>().Single(s => s.StationCode == "HB");
        var lt = context.Set<Station>().Single(s => s.StationCode == "LT");
        var date = DateOnly.FromDateTime(Now.UtcDateTime);

        var searchResults = await new SearchTripsQueryHandler(context, new FixedTimeProvider(Now))
            .Handle(new SearchTripsQuery(hb.Id, lt.Id, date), CancellationToken.None);
        var searchTrip = searchResults.Single(x => x.TripCode == "TR-BOARD-1");
        searchTrip.AvailableSeats.ShouldBe(2);
        searchTrip.IsBookingClosed.ShouldBeFalse();
        searchTrip.IsBookable.ShouldBeTrue();
        searchTrip.TripStatus.ShouldBe(TripStatus.Boarding.ToString());

        var seatMap = await new GetTripSeatMapQueryHandler(context, userContext, new FixedTimeProvider(Now))
            .Handle(new GetTripSeatMapQuery(seeded.Trip.Id, "HB", "LT"), CancellationToken.None);
        seatMap.IsBookingClosed.ShouldBeFalse();

        var booking = await CreateHandler(context, userContext).Handle(
            new CreateBookingCommand("TR-BOARD-1", [Adult("A1", "HB", "LT")], null),
            CancellationToken.None);
        booking.BookingId.ShouldNotBe(Guid.Empty);
    }

    [Test]
    public async Task SeatMapAcceptsStationNameOrIdWhenResolvingSegment()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userContext = await SeatFlowTestData.SeedCustomerAsync(context);
        var seeded = await SeedThreeStopTripAsync(context, "TR-SEG-ID", withDistances: true);
        var hb = context.Set<Station>().Single(s => s.StationCode == "HB");
        var lt = context.Set<Station>().Single(s => s.StationCode == "LT");

        var seatMap = await new GetTripSeatMapQueryHandler(context, userContext, new FixedTimeProvider(Now))
            .Handle(new GetTripSeatMapQuery(seeded.Trip.Id, hb.StationName, lt.Id.ToString()), CancellationToken.None);

        seatMap.FromStationCode.ShouldBe("HB");
        seatMap.ToStationCode.ShouldBe("LT");
        seatMap.IsBookingClosed.ShouldBeFalse();
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
    public async Task RegularConcessionTicketsAreFreeEvenWhenOldFareRulesExist()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userContext = await SeatFlowTestData.SeedCustomerAsync(context);
        await SeedThreeStopTripAsync(context, "TR-FARE-FREE", withDistances: true);
        context.Set<TicketFareRule>().AddRange(
            new TicketFareRule
            {
                TicketTypeCode = "CHILD",
                RouteType = RouteTypes.Regular,
                PriceModifier = 0.5m,
                IsActive = true
            },
            new TicketFareRule
            {
                TicketTypeCode = "SENIOR",
                RouteType = RouteTypes.Regular,
                PriceModifier = 0.5m,
                IsActive = true
            },
            new TicketFareRule
            {
                TicketTypeCode = "DISABLED",
                RouteType = RouteTypes.Regular,
                PriceModifier = 0.5m,
                IsActive = true
            },
            new TicketFareRule
            {
                TicketTypeCode = "INFANT",
                RouteType = RouteTypes.Regular,
                PriceModifier = 1.0m,
                IsActive = true
            });
        await context.SaveChangesAsync();
        var handler = CreateHandler(context, userContext);

        var result = await handler.Handle(
            new CreateBookingCommand(
                "TR-FARE-FREE",
                [
                    Adult("A1", "BB", "HB") with { TicketTypeCode = "CHILD" },
                    Adult("A2", "BB", "HB") with { TicketTypeCode = "SENIOR" },
                    Adult("A1", "HB", "LT") with { TicketTypeCode = "DISABLED" },
                    Adult("A2", "HB", "LT") with { TicketTypeCode = "INFANT", BirthYear = 2026 }
                ],
                null),
            CancellationToken.None);

        result.SubtotalAmount.ShouldBe(0m);
        result.TotalAmount.ShouldBe(0m);
        context.Set<BookingPassenger>()
            .Where(p => p.BookingId == result.BookingId)
            .Select(p => p.UnitPrice)
            .ToList()
            .ShouldAllBe(x => x == 0m);
    }

    [Test]
    public async Task MissingDistanceRejectsRegularBookingInsteadOfFallingBackToSeatTypeFare()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userContext = await SeatFlowTestData.SeedCustomerAsync(context);
        await SeedThreeStopTripAsync(context, "TR-FARE-2", withDistances: false);
        var handler = CreateHandler(context, userContext);

        var exception = await Should.ThrowAsync<ValidationException>(() => handler.Handle(
            new CreateBookingCommand("TR-FARE-2", [Adult("A1", "BB", "LT")], null),
            CancellationToken.None));

        // Tuyến Waterbus thường thiếu km phải bị chặn, không được fallback FixedFareCalculator/giá STANDARD.
        exception.Errors.SelectMany(x => x.Value)
            .ShouldContain(m => m.Contains("chưa nhập đủ số km"));
    }

    [Test]
    public async Task MissingDistanceMarksSearchUnavailableAndSeatMapRejects()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userContext = await SeatFlowTestData.SeedCustomerAsync(context);
        var trip = await SeedThreeStopTripAsync(context, "TR-FARE-3", withDistances: false);
        var bb = context.Set<Station>().Single(s => s.StationCode == "BB");
        var lt = context.Set<Station>().Single(s => s.StationCode == "LT");
        var date = DateOnly.FromDateTime(Now.UtcDateTime);

        var searchResults = await new SearchTripsQueryHandler(context, new FixedTimeProvider(Now))
            .Handle(new SearchTripsQuery(bb.Id, lt.Id, date), CancellationToken.None);
        var searchTrip = searchResults.Single(x => x.TripCode == "TR-FARE-3");
        searchTrip.MinPrice.ShouldBeNull();
        searchTrip.IsBookable.ShouldBeFalse();
        searchTrip.IsBookingClosed.ShouldBeFalse();
        searchTrip.BookingClosedReason.ShouldNotBeNull();
        searchTrip.BookingClosedReason!.ShouldContain("chưa nhập đủ số km");

        var seatMapHandler = new GetTripSeatMapQueryHandler(
            context, userContext, new FixedTimeProvider(Now));
        var exception = await Should.ThrowAsync<ValidationException>(() => seatMapHandler.Handle(
            new GetTripSeatMapQuery(trip.Trip.Id, "BB", "LT"), CancellationToken.None));
        exception.Errors.SelectMany(x => x.Value)
            .ShouldContain(m => m.Contains("chưa nhập đủ số km"));
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

        // Chặng HB→LT: khách trước đã xuống ở HB → cả 2 ghế trống; giá "từ" lấy loại vé trả tiền rẻ nhất (ADULT).
        var tailResults = await searchHandler.Handle(
            new SearchTripsQuery(hb.Id, lt.Id, date), CancellationToken.None);
        var tailTrip = tailResults.Single(x => x.TripCode == "TR-SRCH-1");
        tailTrip.AvailableSeats.ShouldBe(2);
        tailTrip.MinPrice.ShouldBe(11000m);

        // Chặng BB→HB (giao vé đã bán): còn 1 ghế; giá "từ" lấy loại vé trả tiền rẻ nhất (ADULT).
        var headResults = await searchHandler.Handle(
            new SearchTripsQuery(bb.Id, hb.Id, date), CancellationToken.None);
        var headTrip = headResults.Single(x => x.TripCode == "TR-SRCH-1");
        headTrip.AvailableSeats.ShouldBe(1);
        headTrip.MinPrice.ShouldBe(9000m);
    }

    [Test]
    public async Task SearchTripsReturnsSegmentTimesFromTripStops()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userContext = await SeatFlowTestData.SeedCustomerAsync(context);
        var seeded = await SeedThreeStopTripAsync(context, "TR-TIME-1", withDistances: true);
        var trip = seeded.Trip;

        var bb = context.Set<Station>().Single(s => s.StationCode == "BB");
        var hb = context.Set<Station>().Single(s => s.StationCode == "HB");
        var lt = context.Set<Station>().Single(s => s.StationCode == "LT");

        // Lịch dừng dự kiến: BB 0' → HB +20' → LT +45'.
        var dep = trip.DepartureTime;
        context.AddRange(
            new TripStop { TripId = trip.Id, StationId = bb.Id, StopOrder = 1, PlannedArrivalTime = dep, PlannedDepartureTime = dep },
            new TripStop { TripId = trip.Id, StationId = hb.Id, StopOrder = 2, PlannedArrivalTime = dep.AddMinutes(20), PlannedDepartureTime = dep.AddMinutes(20) },
            new TripStop { TripId = trip.Id, StationId = lt.Id, StopOrder = 3, PlannedArrivalTime = dep.AddMinutes(45), PlannedDepartureTime = dep.AddMinutes(45) });
        await context.SaveChangesAsync();

        var searchHandler = new SearchTripsQueryHandler(context, new FixedTimeProvider(Now));
        var date = DateOnly.FromDateTime(Now.UtcDateTime);

        // Chặng giữa HB→LT: giờ đi/đến phải là giờ tại bến của CHẶNG, không phải đầu/cuối nguyên chuyến.
        var tail = (await searchHandler.Handle(
            new SearchTripsQuery(hb.Id, lt.Id, date), CancellationToken.None))
            .Single(x => x.TripCode == "TR-TIME-1");
        tail.FromStopScheduledDeparture.ShouldBe(dep.AddMinutes(20));
        tail.ToStopScheduledArrival.ShouldBe(dep.AddMinutes(45));
        tail.DepartureTime.ShouldBe(dep);

        var head = (await searchHandler.Handle(
            new SearchTripsQuery(bb.Id, hb.Id, date), CancellationToken.None))
            .Single(x => x.TripCode == "TR-TIME-1");
        head.FromStopScheduledDeparture.ShouldBe(dep);
        head.ToStopScheduledArrival.ShouldBe(dep.AddMinutes(20));
    }

    [Test]
    public async Task SearchTripsUsesNextOpenSegmentWhenStationsRepeat()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userContext = await SeatFlowTestData.SeedCustomerAsync(context);
        var bb = await GetOrCreateStationAsync(context, "BB");
        var hb = await GetOrCreateStationAsync(context, "HB");
        var lt = await GetOrCreateStationAsync(context, "LT");

        var route = new Route
        {
            RouteCode = "R-REPEAT",
            RouteName = "Repeated stops",
            RouteType = RouteTypes.Regular,
            IsBookable = true
        };
        route.RouteStops.Add(new RouteStop { Route = route, Station = bb, StationId = bb.Id, StopOrder = 1 });
        route.RouteStops.Add(new RouteStop { Route = route, Station = hb, StationId = hb.Id, StopOrder = 2, DistanceFromPreviousKm = 1 });
        route.RouteStops.Add(new RouteStop { Route = route, Station = bb, StationId = bb.Id, StopOrder = 3, DistanceFromPreviousKm = 1 });
        route.RouteStops.Add(new RouteStop { Route = route, Station = lt, StationId = lt.Id, StopOrder = 4, DistanceFromPreviousKm = 1 });
        route.RouteStops.Add(new RouteStop { Route = route, Station = hb, StationId = hb.Id, StopOrder = 5, DistanceFromPreviousKm = 1 });
        route.RouteStops.Add(new RouteStop { Route = route, Station = lt, StationId = lt.Id, StopOrder = 6, DistanceFromPreviousKm = 1 });

        var boat = SeatFlowTestData.Boat(SeatSetupType.FullStandard, seatsConfigured: true, BoatStatus.Active);
        boat.SeatCount = 1;
        var seat = new Seat { Boat = boat, BoatId = boat.Id, Code = "A1", Deck = 1, Row = "A", Column = 1 };
        var trip = new Trip
        {
            Route = route,
            RouteId = route.Id,
            Boat = boat,
            BoatId = boat.Id,
            TripCode = "TR-REPEAT",
            TripType = TripTypes.Regular,
            OperatingDate = DateOnly.FromDateTime(Now.UtcDateTime),
            DepartureTime = Now,
            ArrivalTime = Now.AddMinutes(31),
            CapacitySnapshot = 1,
            TripStatus = TripStatus.InProgress
        };
        var tripSeat = new TripSeat { Trip = trip, TripId = trip.Id, Seat = seat, SeatId = seat.Id, Price = 10000m };
        context.AddRange(route, boat, seat, trip, tripSeat);
        context.AddRange(
            new TripStop { Trip = trip, TripId = trip.Id, Station = bb, StationId = bb.Id, StopOrder = 1, PlannedDepartureTime = Now },
            new TripStop { Trip = trip, TripId = trip.Id, Station = hb, StationId = hb.Id, StopOrder = 2, PlannedArrivalTime = Now.AddMinutes(1), PlannedDepartureTime = Now.AddMinutes(1) },
            new TripStop { Trip = trip, TripId = trip.Id, Station = bb, StationId = bb.Id, StopOrder = 3, PlannedArrivalTime = Now.AddMinutes(2), PlannedDepartureTime = Now.AddMinutes(2) },
            new TripStop { Trip = trip, TripId = trip.Id, Station = lt, StationId = lt.Id, StopOrder = 4, PlannedArrivalTime = Now.AddMinutes(5), PlannedDepartureTime = Now.AddMinutes(5) },
            new TripStop { Trip = trip, TripId = trip.Id, Station = hb, StationId = hb.Id, StopOrder = 5, PlannedArrivalTime = Now.AddMinutes(30), PlannedDepartureTime = Now.AddMinutes(30) },
            new TripStop { Trip = trip, TripId = trip.Id, Station = lt, StationId = lt.Id, StopOrder = 6, PlannedArrivalTime = Now.AddMinutes(31) });
        await context.SaveChangesAsync();

        var results = await new SearchTripsQueryHandler(context, new FixedTimeProvider(Now))
            .Handle(new SearchTripsQuery(hb.Id, lt.Id, DateOnly.FromDateTime(Now.UtcDateTime)), CancellationToken.None);

        var result = results.Single(x => x.TripCode == "TR-REPEAT");
        result.FromStopScheduledDeparture.ShouldBe(Now.AddMinutes(30));
        result.ToStopScheduledArrival.ShouldBe(Now.AddMinutes(31));
        result.AvailableSeats.ShouldBe(1);
        result.IsBookable.ShouldBeTrue();

        var seatMap = await new GetTripSeatMapQueryHandler(context, userContext, new FixedTimeProvider(Now))
            .Handle(new GetTripSeatMapQuery(trip.Id, "HB", "LT"), CancellationToken.None);
        seatMap.IsBookingClosed.ShouldBeFalse();

        var booking = await CreateHandler(context, userContext).Handle(
            new CreateBookingCommand("TR-REPEAT", [Adult("A1", "HB", "LT")], null),
            CancellationToken.None);
        var passenger = context.Set<BookingPassenger>().Single(x => x.BookingId == booking.BookingId);
        passenger.FromStopOrder.ShouldBe(5);
        passenger.ToStopOrder.ShouldBe(6);
    }

    [Test]
    public async Task SearchTripsDerivesSegmentTimesWhenTripHasNoTripStops()
    {
        await using var context = SeatFlowTestData.CreateContext();
        await SeatFlowTestData.SeedCustomerAsync(context);
        var seeded = await SeedThreeStopTripAsync(context, "TR-TIME-2", withDistances: true);

        var hb = context.Set<Station>().Single(s => s.StationCode == "HB");
        var lt = context.Set<Station>().Single(s => s.StationCode == "LT");
        var searchHandler = new SearchTripsQueryHandler(context, new FixedTimeProvider(Now));

        // Không có trip_stops → suy từ route stops (StandardTravelMin null → mặc định 15'/chặng).
        var tail = (await searchHandler.Handle(
                new SearchTripsQuery(hb.Id, lt.Id, DateOnly.FromDateTime(Now.UtcDateTime)), CancellationToken.None))
            .Single(x => x.TripCode == "TR-TIME-2");
        tail.FromStopScheduledDeparture.ShouldBe(seeded.Trip.DepartureTime.AddMinutes(15));
        tail.ToStopScheduledArrival.ShouldBe(seeded.Trip.DepartureTime.AddMinutes(30));
    }

    [Test]
    public async Task BookingDetailShowsSegmentStationsAndTimes()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userContext = await SeatFlowTestData.SeedCustomerAsync(context);
        var seeded = await SeedThreeStopTripAsync(context, "TR-DET-1", withDistances: true);
        var dep = await SeedTripStopsAsync(context, seeded.Trip);

        var booking = await CreateHandler(context, userContext).Handle(
            new CreateBookingCommand("TR-DET-1", [Adult("A1", "HB", "LT")], null),
            CancellationToken.None);

        var detail = await new GetBookingDetailQueryHandler(context, userContext)
            .Handle(new GetBookingDetailQuery(booking.BookingId), CancellationToken.None);

        // Ga + giờ theo CHẶNG của hành khách, không phải đầu/cuối nguyên chuyến.
        var item = detail.Items.ShouldHaveSingleItem();
        item.FromStationName.ShouldBe("Station HB");
        item.ToStationName.ShouldBe("Station LT");
        item.ScheduledDeparture.ShouldBe(dep.AddMinutes(20));
        item.ScheduledArrival.ShouldBe(dep.AddMinutes(45));
    }

    [Test]
    public async Task ScannedTicketShowsPassengerSegmentStationsAndTimes()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userContext = await SeatFlowTestData.SeedCustomerAsync(context);
        var staffContext = await SeatFlowTestData.SeedStaffAsync(context);
        var seeded = await SeedThreeStopTripAsync(context, "TR-SCAN-1", withDistances: true);
        var dep = await SeedTripStopsAsync(context, seeded.Trip);

        var booking = await CreateHandler(context, userContext).Handle(
            new CreateBookingCommand("TR-SCAN-1", [Adult("A1", "HB", "LT")], null),
            CancellationToken.None);
        var passenger = context.Set<BookingPassenger>().Single(p => p.BookingId == booking.BookingId);
        var ticket = new Ticket
        {
            BookingId = booking.BookingId,
            BookingPassengerId = passenger.Id,
            TicketCode = "TK-SCAN-1",
            QrToken = "QR-SCAN-1",
            TicketStatus = TicketStatus.Active,
            IssuedAt = Now
        };
        context.Add(ticket);
        await context.SaveChangesAsync();
        await AddOnBoardAssignmentAsync(context, staffContext.UserId!.Value, seeded.Trip.BoatId!.Value);

        var dto = await new Application.Tickets.ScanTicketQueryHandler(
                context, staffContext, new FixedTimeProvider(Now))
            .Handle(new Application.Tickets.ScanTicketQuery(ticket.QrToken), CancellationToken.None);

        // Staff quét vé phải thấy đúng ga lên/xuống + giờ theo chặng trên vé.
        dto.FromStationName.ShouldBe("Station HB");
        dto.ToStationName.ShouldBe("Station LT");
        dto.ScheduledDeparture.ShouldBe(dep.AddMinutes(20));
        dto.ScheduledArrival.ShouldBe(dep.AddMinutes(45));
        dto.StartTime.ShouldBe(TimeOnly.FromDateTime(dep.AddMinutes(20).LocalDateTime));
    }

    [Test]
    public async Task BookingHistoryShowsBookingSegmentNotWholeRoute()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userContext = await SeatFlowTestData.SeedCustomerAsync(context);
        var seeded = await SeedThreeStopTripAsync(context, "TR-HIST-1", withDistances: true);
        var dep = await SeedTripStopsAsync(context, seeded.Trip);

        await CreateHandler(context, userContext).Handle(
            new CreateBookingCommand("TR-HIST-1", [Adult("A1", "HB", "LT")], null),
            CancellationToken.None);

        var items = await new Application.BookingHistory.GetMyBookingHistoryQueryHandler(context, userContext)
            .Handle(new Application.BookingHistory.GetMyBookingHistoryQuery(), CancellationToken.None);

        var item = items.ShouldHaveSingleItem();
        item.FromStationName.ShouldBe("Station HB");
        item.ToStationName.ShouldBe("Station LT");
        item.DepartureTime.ShouldBe(TimeOnly.FromDateTime(dep.AddMinutes(20).LocalDateTime));
    }

    /// <summary>Gắn trip_stops cho trip 3 trạm: BB +0' → HB +20' → LT +45'. Trả về giờ khởi hành.</summary>
    private static async Task<DateTimeOffset> SeedTripStopsAsync(ApplicationDbContext context, Trip trip)
    {
        var bb = context.Set<Station>().Single(s => s.StationCode == "BB");
        var hb = context.Set<Station>().Single(s => s.StationCode == "HB");
        var lt = context.Set<Station>().Single(s => s.StationCode == "LT");
        var dep = trip.DepartureTime;
        context.AddRange(
            new TripStop { TripId = trip.Id, StationId = bb.Id, StopOrder = 1, PlannedDepartureTime = dep },
            new TripStop { TripId = trip.Id, StationId = hb.Id, StopOrder = 2, PlannedArrivalTime = dep.AddMinutes(20), PlannedDepartureTime = dep.AddMinutes(20) },
            new TripStop { TripId = trip.Id, StationId = lt.Id, StopOrder = 3, PlannedArrivalTime = dep.AddMinutes(45) });
        await context.SaveChangesAsync();
        return dep;
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
    public void CalculateFareRoundsUpBaseFarePlusDistancePrice()
    {
        var policy = new FarePolicyDto(null, 5000m, 1500m, 1000m, "VND");
        DistanceFareSupport.CalculateFare(policy, 2.5m).ShouldBe(9000m);
        DistanceFareSupport.CalculateFare(policy, 6m).ShouldBe(14000m);
        DistanceFareSupport.CalculateFare(policy, 1m).ShouldBe(7000m);
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

    private sealed class RecordingTripSeatNotifier : ITripSeatNotifier
    {
        public List<(Guid TripId, IReadOnlyList<TripSeatStatusChange> Changes)> Published { get; } = [];

        public Task PublishSeatStatusChangedAsync(
            Guid tripId,
            IReadOnlyList<TripSeatStatusChange> changes,
            CancellationToken cancellationToken)
        {
            Published.Add((tripId, changes));
            return Task.CompletedTask;
        }
    }

    private static CreateBookingCommandHandler CreateHandler(
        ApplicationDbContext context,
        TestUserContext userContext,
        DateTimeOffset? now = null) =>
        new(
            context,
            userContext,
            new SequentialBookingCodeGenerator(),
            new FixedFareCalculator(10000m),
            new FixedTimeProvider(now ?? Now));

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

    private static async Task AddOnBoardAssignmentAsync(
        ApplicationDbContext context,
        Guid staffUserId,
        Guid boatId)
    {
        context.StaffWorkAssignments.Add(new StaffWorkAssignment
        {
            StaffUserId = staffUserId,
            AssignmentType = StaffWorkAssignmentType.Boat,
            BoatId = boatId,
            WorkingDate = DateOnly.FromDateTime(Now.UtcDateTime),
            StartAt = Now.AddHours(-1),
            EndAt = Now.AddHours(4),
            Status = StaffWorkAssignmentStatus.Scheduled,
            AssignedByUserId = staffUserId,
            AssignedAt = Now.AddHours(-2)
        });
        await context.SaveChangesAsync();
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
