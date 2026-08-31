using NUnit.Framework;
using SaigonWaterbus.Application.Bookings;
using SaigonWaterbus.Application.Tickets;
using SaigonWaterbus.Application.Trips;
using SaigonWaterbus.Application.UnitTests.TestInfrastructure;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using SaigonWaterbus.Infrastructure.Data;
using Shouldly;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.UnitTests.Bookings;

public class RoundTripCheckInAndScanTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 15, 6, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task CheckInAllWithTripCodeChecksInOnlyThatLeg()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var staffContext = await SeatFlowTestData.SeedStaffAsync(context);
        var seeded = await SeedConfirmedRoundTripBookingAsync(context);
        await AddOnBoardAssignmentAsync(context, staffContext.UserId!.Value, seeded.Booking.Trip!.BoatId!.Value);
        var handler = new CheckInAllBookingTicketsCommandHandler(
            context, staffContext, new FixedTimeProvider(seeded.Booking.Trip!.DepartureTime));

        var manifest = await handler.Handle(
            new CheckInAllBookingTicketsCommand(seeded.Booking.CharterBookingQrToken!, "TR-OUT"),
            CancellationToken.None);

        // Chỉ vé chiều đi được check-in, vé chiều về vẫn Active.
        context.Tickets.Single(x => x.Id == seeded.OutboundTicket.Id)
            .TicketStatus.ShouldBe(TicketStatus.CheckedIn);
        context.Tickets.Single(x => x.Id == seeded.ReturnTicket.Id)
            .TicketStatus.ShouldBe(TicketStatus.Active);
        var checkInEvent = context.TicketScanEvents.Single();
        checkInEvent.TicketId.ShouldBe(seeded.OutboundTicket.Id);
        checkInEvent.Action.ShouldBe(TicketScanAction.CheckIn);
        checkInEvent.Result.ShouldBe(TicketScanResult.Success);
        manifest.ReturnTripCode.ShouldBe("TR-RET");
        var outboundManifestPassenger = manifest.Passengers.Single(p => p.TripCode == "TR-OUT");
        outboundManifestPassenger.TicketStatus
            .ShouldBe(nameof(TicketStatus.CheckedIn));
        outboundManifestPassenger.BookingCode.ShouldBe("BK-ROUNDTRIP");
        outboundManifestPassenger.TicketTypeCode.ShouldBe("ADULT");
        outboundManifestPassenger.TicketTypeName.ShouldBe("Vé người lớn");
        outboundManifestPassenger.UnitPrice.ShouldBe(10000);
        outboundManifestPassenger.CanCheckIn.ShouldBeFalse();
        outboundManifestPassenger.CanCheckOut.ShouldBeFalse();
        outboundManifestPassenger.CheckedOutAt.ShouldBeNull();
        var returnManifestPassenger = manifest.Passengers.Single(p => p.TripCode == "TR-RET");
        returnManifestPassenger.TicketStatus
            .ShouldBe(nameof(TicketStatus.Active));
        returnManifestPassenger.CanCheckIn.ShouldBeFalse();
        returnManifestPassenger.CanCheckOut.ShouldBeFalse();
    }

    [Test]
    public async Task CheckInAllWithoutTripCodeRejectsRoundTripLegOutsideBoardingWindow()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var staffContext = await SeatFlowTestData.SeedStaffAsync(context);
        var seeded = await SeedConfirmedRoundTripBookingAsync(context);
        await AddOnBoardAssignmentAsync(context, staffContext.UserId!.Value, seeded.Booking.Trip!.BoatId!.Value);
        var handler = new CheckInAllBookingTicketsCommandHandler(
            context, staffContext, new FixedTimeProvider(seeded.Booking.Trip!.DepartureTime.AddMinutes(-5)));

        await Should.ThrowAsync<ValidationException>(() => handler.Handle(
            new CheckInAllBookingTicketsCommand(seeded.Booking.CharterBookingQrToken!),
            CancellationToken.None));

        context.Tickets.Count(x => x.TicketStatus == TicketStatus.CheckedIn).ShouldBe(0);
    }

    [Test]
    public async Task CheckInAllAllowsExactlyTwoMinutesAfterDepartureButRejectsOneSecondLater()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var staffContext = await SeatFlowTestData.SeedStaffAsync(context);
        var accepted = await SeedConfirmedRoundTripBookingAsync(context);
        await AddOnBoardAssignmentAsync(context, staffContext.UserId!.Value, accepted.Booking.Trip!.BoatId!.Value);

        var acceptedHandler = new CheckInAllBookingTicketsCommandHandler(
            context,
            staffContext,
            new FixedTimeProvider(accepted.Booking.Trip.DepartureTime.AddMinutes(2)));
        await acceptedHandler.Handle(
            new CheckInAllBookingTicketsCommand(accepted.Booking.CharterBookingQrToken!, "TR-OUT"),
            CancellationToken.None);
        accepted.OutboundTicket.TicketStatus.ShouldBe(TicketStatus.CheckedIn);

        var rejected = await SeedConfirmedRoundTripBookingAsync(context);
        await AddOnBoardAssignmentAsync(context, staffContext.UserId!.Value, rejected.Booking.Trip!.BoatId!.Value);
        var rejectedHandler = new CheckInAllBookingTicketsCommandHandler(
            context,
            staffContext,
            new FixedTimeProvider(rejected.Booking.Trip!.DepartureTime.AddMinutes(2).AddSeconds(1)));
        await Should.ThrowAsync<ValidationException>(() => rejectedHandler.Handle(
            new CheckInAllBookingTicketsCommand(rejected.Booking.CharterBookingQrToken!, "TR-OUT"),
            CancellationToken.None));
        rejected.OutboundTicket.TicketStatus.ShouldBe(TicketStatus.Active);
    }

    [Test]
    public async Task CheckOutAllWithTripCodeChecksOutOnlyThatLegAndCompletesAfterBothLegs()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var staffContext = await SeatFlowTestData.SeedStaffAsync(context);
        var seeded = await SeedConfirmedRoundTripBookingAsync(context);
        await AddOnBoardAssignmentAsync(context, staffContext.UserId!.Value, seeded.Booking.Trip!.BoatId!.Value);

        seeded.OutboundTicket.TicketStatus = TicketStatus.CheckedIn;
        seeded.OutboundTicket.CheckedInAt = seeded.Booking.Trip!.DepartureTime.AddMinutes(-5);
        seeded.ReturnTicket.TicketStatus = TicketStatus.CheckedIn;
        seeded.ReturnTicket.CheckedInAt = seeded.Booking.ReturnTrip!.DepartureTime.AddMinutes(-5);
        await context.SaveChangesAsync();

        var checkOutHandler = new CheckOutAllBookingTicketsCommandHandler(
            context,
            staffContext,
            new FixedTimeProvider(seeded.Booking.Trip.ArrivalTime.AddMinutes(5)));

        var outboundManifest = await checkOutHandler.Handle(
            new CheckOutAllBookingTicketsCommand(seeded.Booking.CharterBookingQrToken!, "TR-OUT"),
            CancellationToken.None);

        context.Tickets.Single(x => x.Id == seeded.OutboundTicket.Id)
            .TicketStatus.ShouldBe(TicketStatus.CheckedOut);
        context.Tickets.Single(x => x.Id == seeded.ReturnTicket.Id)
            .TicketStatus.ShouldBe(TicketStatus.CheckedIn);
        context.Set<Booking>().Single().BookingStatus.ShouldBe(BookingStatus.Confirmed);
        context.TicketScanEvents.Single().TicketId.ShouldBe(seeded.OutboundTicket.Id);
        context.TicketScanEvents.Single().Action.ShouldBe(TicketScanAction.CheckOut);
        outboundManifest.Passengers.Single(p => p.TripCode == "TR-OUT")
            .CanCheckOut.ShouldBeFalse();
        outboundManifest.Passengers.Single(p => p.TripCode == "TR-RET")
            .CanCheckOut.ShouldBeFalse();

        var returnManifest = await new CheckOutAllBookingTicketsCommandHandler(
                context,
                staffContext,
                new FixedTimeProvider(seeded.Booking.ReturnTrip.ArrivalTime.AddMinutes(5)))
            .Handle(
                new CheckOutAllBookingTicketsCommand(seeded.Booking.CharterBookingQrToken!, "TR-RET"),
                CancellationToken.None);

        context.Tickets.Count(x => x.TicketStatus == TicketStatus.CheckedOut).ShouldBe(2);
        context.TicketScanEvents.Count(x => x.Action == TicketScanAction.CheckOut).ShouldBe(2);
        context.TicketScanEvents.Select(x => x.TicketId).ShouldBe(
            [seeded.OutboundTicket.Id, seeded.ReturnTicket.Id],
            ignoreOrder: true);
        context.Set<Booking>().Single().BookingStatus.ShouldBe(BookingStatus.Completed);
        returnManifest.BookingStatus.ShouldBe(nameof(BookingStatus.Completed));
        returnManifest.CheckedInTicketCount.ShouldBe(0);
    }

    [Test]
    public async Task CheckOutAllOpensExactlyThreeMinutesBeforeArrival()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var staffContext = await SeatFlowTestData.SeedStaffAsync(context);
        var accepted = await SeedConfirmedRoundTripBookingAsync(context);
        await AddOnBoardAssignmentAsync(context, staffContext.UserId!.Value, accepted.Booking.Trip!.BoatId!.Value);
        accepted.OutboundTicket.TicketStatus = TicketStatus.CheckedIn;
        accepted.OutboundTicket.CheckedInAt = accepted.Booking.Trip.DepartureTime;
        await context.SaveChangesAsync();

        var acceptedHandler = new CheckOutAllBookingTicketsCommandHandler(
            context,
            staffContext,
            new FixedTimeProvider(accepted.Booking.Trip.ArrivalTime.AddMinutes(-3)));
        await acceptedHandler.Handle(
            new CheckOutAllBookingTicketsCommand(accepted.Booking.CharterBookingQrToken!, "TR-OUT"),
            CancellationToken.None);
        accepted.OutboundTicket.TicketStatus.ShouldBe(TicketStatus.CheckedOut);

        var rejected = await SeedConfirmedRoundTripBookingAsync(context);
        await AddOnBoardAssignmentAsync(context, staffContext.UserId!.Value, rejected.Booking.Trip!.BoatId!.Value);
        rejected.OutboundTicket.TicketStatus = TicketStatus.CheckedIn;
        rejected.OutboundTicket.CheckedInAt = rejected.Booking.Trip!.DepartureTime;
        await context.SaveChangesAsync();
        var rejectedHandler = new CheckOutAllBookingTicketsCommandHandler(
            context,
            staffContext,
            new FixedTimeProvider(rejected.Booking.Trip.ArrivalTime.AddMinutes(-3).AddSeconds(-1)));
        await Should.ThrowAsync<ValidationException>(() => rejectedHandler.Handle(
            new CheckOutAllBookingTicketsCommand(rejected.Booking.CharterBookingQrToken!, "TR-OUT"),
            CancellationToken.None));
        rejected.OutboundTicket.TicketStatus.ShouldBe(TicketStatus.CheckedIn);
    }

    [Test]
    public async Task CheckInAllWithForeignTripCodeIsRejected()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var staffContext = await SeatFlowTestData.SeedStaffAsync(context);
        var seeded = await SeedConfirmedRoundTripBookingAsync(context);
        var handler = new CheckInAllBookingTicketsCommandHandler(
            context, staffContext, new FixedTimeProvider(Now));

        await Should.ThrowAsync<ValidationException>(() => handler.Handle(
            new CheckInAllBookingTicketsCommand(seeded.Booking.CharterBookingQrToken!, "TR-KHAC"),
            CancellationToken.None));
    }

    [Test]
    public async Task StaffAssignedToAnotherBoatCannotOpenRegularBookingManifest()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var staffContext = await SeatFlowTestData.SeedStaffAsync(context);
        var seeded = await SeedConfirmedRoundTripBookingAsync(context);
        var otherBoat = new Boat
        {
            Code = "BOAT-OTHER",
            Name = "Other boat",
            Status = BoatStatus.Active,
            SeatCount = 2,
            NumberOfDecks = 1,
            SeatsConfigured = true
        };
        context.Boats.Add(otherBoat);
        await context.SaveChangesAsync();
        await AddOnBoardAssignmentAsync(context, staffContext.UserId!.Value, otherBoat.Id);

        var handler = new GetBookingManifestQueryHandler(
            context,
            staffContext,
            new FixedTimeProvider(Now));

        await Should.ThrowAsync<ValidationException>(() => handler.Handle(
            new GetBookingManifestByQrTokenQuery(seeded.Booking.CharterBookingQrToken!),
            CancellationToken.None));
    }

    [Test]
    public async Task ScannedReturnLegTicketShowsReturnTripAndOnlyLegPassengers()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var staffContext = await SeatFlowTestData.SeedStaffAsync(context);
        var seeded = await SeedConfirmedRoundTripBookingAsync(context);
        await AddOnBoardAssignmentAsync(context, staffContext.UserId!.Value, seeded.Booking.Trip!.BoatId!.Value);
        var handler = new ScanTicketQueryHandler(context, staffContext, new FixedTimeProvider(Now));

        var result = await handler.Handle(
            new ScanTicketQuery(seeded.ReturnTicket.QrToken),
            CancellationToken.None);

        result.TripCode.ShouldBe("TR-RET");
        result.Passengers.ShouldHaveSingleItem();
        result.Passengers.Single().FullName.ShouldBe("Khach Chieu Ve");

        var outboundResult = await handler.Handle(
            new ScanTicketQuery(seeded.OutboundTicket.QrToken),
            CancellationToken.None);
        outboundResult.TripCode.ShouldBe("TR-OUT");
        outboundResult.Passengers.Single().FullName.ShouldBe("Khach Chieu Di");
    }

    private sealed record SeededRoundTrip(Booking Booking, Ticket OutboundTicket, Ticket ReturnTicket);

    /// <summary>Booking khứ hồi Confirmed + Paid với 1 hành khách/chiều, mỗi người 1 vé Active + QR chung.</summary>
    /// <summary>
    /// Chuyến chiều đi kết thúc thì chỉ vé chiều đi được đóng sổ. Vé chiều về thuộc chuyến khác,
    /// giờ khác — trước đây bị đánh hết hạn theo vì code lấy TOÀN BỘ vé của booking.
    /// </summary>
    [Test]
    public async Task CompletingOutboundTripLeavesReturnLegTicketUntouched()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var seeded = await SeedConfirmedRoundTripBookingAsync(context);
        seeded.OutboundTicket.TicketStatus = TicketStatus.CheckedIn;
        seeded.OutboundTicket.CheckedInAt = seeded.Booking.Trip!.DepartureTime.AddMinutes(-5);
        await context.SaveChangesAsync();

        await new UpdateTripStatusCommandHandler(context).Handle(
            new UpdateTripStatusCommand(seeded.Booking.TripId!.Value, TripStatus.Completed, null),
            CancellationToken.None);

        context.Tickets.Single(x => x.Id == seeded.OutboundTicket.Id)
            .TicketStatus.ShouldBe(TicketStatus.Expired, "khách lên tàu nhưng quên quét ra");
        context.Tickets.Single(x => x.Id == seeded.ReturnTicket.Id)
            .TicketStatus.ShouldBe(TicketStatus.Active, "chuyến chiều về còn chưa chạy");
    }

    /// <summary>Chuyến chiều về kết thúc thì mới đến lượt vé chiều về được đóng sổ.</summary>
    [Test]
    public async Task CompletingReturnTripClosesOnlyReturnLegTicket()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var seeded = await SeedConfirmedRoundTripBookingAsync(context);

        await new UpdateTripStatusCommandHandler(context).Handle(
            new UpdateTripStatusCommand(seeded.Booking.ReturnTripId!.Value, TripStatus.Completed, null),
            CancellationToken.None);

        context.Tickets.Single(x => x.Id == seeded.ReturnTicket.Id)
            .TicketStatus.ShouldBe(TicketStatus.Expired);
        context.Tickets.Single(x => x.Id == seeded.OutboundTicket.Id)
            .TicketStatus.ShouldBe(TicketStatus.Active, "chuyến chiều đi chưa kết thúc");
    }

    /// <summary>
    /// Vé hết hạn phải hiện đúng là "Expired". Trước đây nó bị lọc khỏi danh sách vé, TicketStatus
    /// về null, và màn hình khách rơi về trạng thái booking — hiện "ĐÃ XÁC NHẬN" cho một tấm vé
    /// đã chết, kèm câu "vé sẽ hiện sau khi thanh toán".
    /// </summary>
    [Test]
    public async Task ExpiredTicketIsReportedInsteadOfBeingHidden()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var seeded = await SeedConfirmedRoundTripBookingAsync(context);
        var ownerId = Guid.NewGuid();
        seeded.Booking.UserId = ownerId;
        seeded.Booking.PaymentStatus = "Paid";
        seeded.ReturnTicket.TicketStatus = TicketStatus.Expired;
        await context.SaveChangesAsync();

        // Ghim đồng hồ trước giờ chạy: vé Active của chuyến đã qua sẽ hiển thị thành "Used",
        // che mất điều test này muốn kiểm.
        var detail = await new GetBookingDetailQueryHandler(
                context, new TestUserContext(ownerId), new FixedTimeProvider(Now))
            .Handle(new GetBookingDetailQuery(seeded.Booking.Id), CancellationToken.None);

        var returnItem = detail.Items.Single(x => x.TripCode == "TR-RET");
        returnItem.TicketStatus.ShouldBe(nameof(TicketStatus.Expired));
        returnItem.TicketCode.ShouldBe(seeded.ReturnTicket.TicketCode);

        detail.Items.Single(x => x.TripCode == "TR-OUT")
            .TicketStatus.ShouldBe(nameof(TicketStatus.Active), "vé chiều đi không bị ảnh hưởng");
    }

    private static async Task<SeededRoundTrip> SeedConfirmedRoundTripBookingAsync(ApplicationDbContext context)
    {
        var boat = new Boat
        {
            Code = "BOAT-ROUND",
            Name = "Round trip boat",
            Status = BoatStatus.Active,
            SeatCount = 2,
            NumberOfDecks = 1,
            SeatsConfigured = true
        };
        var outboundTrip = CreateTrip("TR-OUT", Now.AddHours(2), boat);
        var returnTrip = CreateTrip("TR-RET", Now.AddHours(6), boat);

        var booking = new Booking
        {
            Trip = outboundTrip,
            TripId = outboundTrip.Id,
            ReturnTrip = returnTrip,
            ReturnTripId = returnTrip.Id,
            BookingCode = "BK-ROUNDTRIP",
            CharterBookingQrToken = "BK" + Guid.NewGuid().ToString("N"),
            ContactName = "Nguyen Van A",
            ContactPhone = "0900000000",
            ContactEmail = "booker@example.test",
            BookingStatus = BookingStatus.Confirmed,
            PaymentStatus = "Paid",
            SubtotalAmount = 20000,
            TotalAmount = 20000,
            RemainingAmount = 0
        };

        var outboundPassenger = new BookingPassenger
        {
            Booking = booking,
            BookingId = booking.Id,
            TripId = outboundTrip.Id,
            Trip = outboundTrip,
            FullName = "Khach Chieu Di",
            PassengerType = "ADULT",
            UnitPrice = 10000
        };
        var returnPassenger = new BookingPassenger
        {
            Booking = booking,
            BookingId = booking.Id,
            TripId = returnTrip.Id,
            Trip = returnTrip,
            FullName = "Khach Chieu Ve",
            PassengerType = "ADULT",
            UnitPrice = 10000
        };

        var outboundTicket = CreateTicket(booking, outboundPassenger, "TK-OUT-01");
        var returnTicket = CreateTicket(booking, returnPassenger, "TK-RET-01");

        context.AddRange(
            boat, outboundTrip, returnTrip, booking,
            outboundPassenger, returnPassenger,
            outboundTicket, returnTicket);
        await context.SaveChangesAsync();

        return new SeededRoundTrip(booking, outboundTicket, returnTicket);
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
            EndAt = Now.AddHours(12),
            Status = StaffWorkAssignmentStatus.Scheduled,
            AssignedByUserId = staffUserId,
            AssignedAt = Now.AddHours(-2)
        });
        await context.SaveChangesAsync();
    }

    private static Trip CreateTrip(string tripCode, DateTimeOffset departureTime, Boat boat) =>
        new()
        {
            Boat = boat,
            BoatId = boat.Id,
            Route = new Route
            {
                RouteCode = $"R-{tripCode}",
                RouteName = $"Route {tripCode}"
            },
            TripCode = tripCode,
            OperatingDate = DateOnly.FromDateTime(departureTime.UtcDateTime),
            DepartureTime = departureTime,
            ArrivalTime = departureTime.AddHours(1),
            CapacitySnapshot = 2
        };

    private static Ticket CreateTicket(Booking booking, BookingPassenger passenger, string ticketCode) =>
        new()
        {
            Booking = booking,
            BookingId = booking.Id,
            BookingPassengerId = passenger.Id,
            BookingPassenger = passenger,
            TicketCode = ticketCode,
            QrToken = $"QR-{ticketCode}-{Guid.NewGuid():N}",
            TicketStatus = TicketStatus.Active,
            IssuedAt = Now
        };
}
