using MediatR;
using NUnit.Framework;
using SaigonWaterbus.Application.Bookings;
using SaigonWaterbus.Application.Common.Exceptions;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Application.Fares;
using SaigonWaterbus.Application.Payments;
using SaigonWaterbus.Application.Points;
using SaigonWaterbus.Application.UnitTests.TestInfrastructure;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using SaigonWaterbus.Infrastructure.Data;
using Shouldly;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.UnitTests.Bookings;

/// <summary>
/// Bán vé tại quầy: staff bán cho khách vãng lai (không có tài khoản), thu tiền mặt xác nhận
/// ngay, và bán được cả khi tàu đã rời bến.
/// </summary>
public class CounterBookingTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 15, 4, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task CashSaleConfirmsBookingAndIssuesTicketsImmediately()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var staffContext = await SeatFlowTestData.SeedStaffAsync(context);
        await SeedTripAsync(context, "TR-CTR-1", TripStatus.Scheduled);
        var notifications = new RecordingPaymentNotificationSender();
        var handler = CreateHandler(context, staffContext, notifications);

        var result = await handler.Handle(CashCommand("TR-CTR-1", "A1"), CancellationToken.None);

        result.BookingStatus.ShouldBe(nameof(BookingStatus.Confirmed));
        result.PaymentStatus.ShouldBe("Paid");
        result.PaymentMethod.ShouldBe("Cash");
        result.PaidAt.ShouldBe(Now);
        result.CheckoutUrl.ShouldBeNull();
        result.Manifest.ShouldNotBeNull();
        result.Manifest!.BookingQrToken.ShouldNotBeNullOrWhiteSpace();
        result.Manifest.Passengers.Single().TicketCode.ShouldNotBeNullOrWhiteSpace();
        result.Manifest.Passengers.Single().QrToken.ShouldNotBeNullOrWhiteSpace();
        notifications.ETickets.Count.ShouldBe(1);
        notifications.ETickets.Single().Booking.Email.ShouldBe("khach@example.test");

        // Vé + QR phải được phát hành ngay để staff in cho khách.
        context.Set<Ticket>().Count(t => t.BookingId == result.BookingId).ShouldBe(1);

        var booking = context.Set<Booking>().Single(b => b.Id == result.BookingId);
        booking.UserId.ShouldBeNull();
        booking.SoldByStaffId.ShouldBe(staffContext.UserId);
        booking.ContactName.ShouldBe("Khach Vang Lai");
        booking.ContactPhone.ShouldBe("0909000111");
        booking.ContactEmail.ShouldBe("khach@example.test");
        booking.RemainingAmount.ShouldBe(0m);

        var payment = context.Set<Payment>().Single(p => p.BookingId == result.BookingId);
        payment.Provider.ShouldBe("Counter");
        payment.PaymentMethod.ShouldBe("Cash");
        payment.Amount.ShouldBe(booking.TotalAmount);
    }

    [Test]
    public async Task ManagerCanSellAtCounterWithCash()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var managerContext = await SeatFlowTestData.SeedManagerAsync(context);
        await SeedTripAsync(context, "TR-CTR-MGR", TripStatus.Scheduled);
        var handler = CreateHandler(context, managerContext);

        var result = await handler.Handle(CashCommand("TR-CTR-MGR", "A1"), CancellationToken.None);

        result.BookingStatus.ShouldBe(nameof(BookingStatus.Confirmed));
        result.PaymentStatus.ShouldBe("Paid");
        result.PaymentMethod.ShouldBe(PaymentSupport.CashPaymentMethod);

        var booking = context.Set<Booking>().Single(b => b.Id == result.BookingId);
        booking.SoldByStaffId.ShouldBe(managerContext.UserId);

        var payment = context.Set<Payment>().Single(p => p.BookingId == result.BookingId);
        payment.Provider.ShouldBe(PaymentSupport.CounterProvider);
        payment.PaymentMethod.ShouldBe(PaymentSupport.CashPaymentMethod);
        payment.PaidAt.ShouldBe(Now);
    }

    [Test]
    public async Task CounterCustomerLookupFindsActiveCustomerByPhoneAndEmail()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var staffContext = await SeatFlowTestData.SeedStaffAsync(context);
        var customerContext = await SeatFlowTestData.SeedCustomerAsync(context);
        var customer = context.Set<User>().Single(u => u.Id == customerContext.UserId);
        customer.FullName = "Nguyen Thi Loyalty";
        customer.PhoneNumber = PhoneRules.ToInternationalFormat("0901234567");
        customer.NormalizedPhoneNumber = customer.PhoneNumber;
        customer.Email = "loyalty@example.test";
        customer.NormalizedEmail = customer.Email.ToUpperInvariant();
        customer.PointBalance = 250;
        await context.SaveChangesAsync();

        var handler = new LookupCounterBookingCustomerQueryHandler(context, staffContext);

        var byPhone = await handler.Handle(new LookupCounterBookingCustomerQuery("0901234567"), CancellationToken.None);
        var byEmail = await handler.Handle(new LookupCounterBookingCustomerQuery("loyalty@example.test"), CancellationToken.None);

        byPhone.Count.ShouldBe(1);
        byPhone.Single().CustomerUserId.ShouldBe(customer.Id);
        byPhone.Single().FullName.ShouldBe("Nguyen Thi Loyalty");
        byPhone.Single().PointBalance.ShouldBe(250);
        byEmail.Single().CustomerUserId.ShouldBe(customer.Id);
    }

    [Test]
    public void CounterCustomerLookupRejectsCustomerCodeKeyword()
    {
        var result = new LookupCounterBookingCustomerQueryValidator()
            .Validate(new LookupCounterBookingCustomerQuery("CU00042"));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(x => x.PropertyName == nameof(LookupCounterBookingCustomerQuery.Keyword));
    }

    [Test]
    public async Task NonStaffCannotLookupCounterCustomers()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var customerContext = await SeatFlowTestData.SeedCustomerAsync(context);
        var handler = new LookupCounterBookingCustomerQueryHandler(context, customerContext);

        await Should.ThrowAsync<ForbiddenAccessException>(() =>
            handler.Handle(new LookupCounterBookingCustomerQuery("0901234567"), CancellationToken.None));
    }

    [Test]
    public async Task CounterSaleCanLinkCustomerAccountForLaterPointAccrual()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var staffContext = await SeatFlowTestData.SeedStaffAsync(context);
        var customerContext = await SeatFlowTestData.SeedCustomerAsync(context);
        await SeedTripAsync(context, "TR-CTR-POINT", TripStatus.Scheduled);
        var handler = CreateHandler(context, staffContext);

        var result = await handler.Handle(
            CashCommand("TR-CTR-POINT", "A1") with
            {
                CustomerUserId = customerContext.UserId,
                CustomerConfirmedForPoints = true
            },
            CancellationToken.None);

        var booking = context.Set<Booking>().Single(b => b.Id == result.BookingId);
        booking.UserId.ShouldBe(customerContext.UserId);
        booking.SoldByStaffId.ShouldBe(staffContext.UserId);
        booking.PointsEarned.ShouldBe(0);
        context.Set<User>().Single(u => u.Id == customerContext.UserId).PointBalance.ShouldBe(0);

        var trip = context.Set<Trip>().Single(t => t.TripCode == "TR-CTR-POINT");
        var oldStatus = trip.TripStatus;
        trip.TripStatus = TripStatus.Completed;
        await PointSupport.AwardCompletionPointsForCompletedTripAsync(
            context,
            trip,
            oldStatus,
            Now.AddHours(3),
            CancellationToken.None);
        await context.SaveChangesAsync();

        booking.PointsEarned.ShouldBe(100);
        context.Set<User>().Single(u => u.Id == customerContext.UserId).PointBalance.ShouldBe(100);
        var transaction = context.Set<PointTransaction>().Single();
        transaction.BookingId.ShouldBe(booking.Id);
        transaction.TransactionType.ShouldBe(PointTransactionTypes.Earn);
        transaction.Points.ShouldBe(100);
    }

    [Test]
    public async Task CounterSaleCanUseConfirmedCustomerPointsAtCounter()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var staffContext = await SeatFlowTestData.SeedStaffAsync(context);
        var customerContext = await SeatFlowTestData.SeedCustomerAsync(context);
        var customer = context.Set<User>().Single(u => u.Id == customerContext.UserId);
        customer.PointBalance = 6_000;
        await SeedTripAsync(context, "TR-CTR-REDEEM", TripStatus.Scheduled);
        await context.SaveChangesAsync();
        var handler = CreateHandler(context, staffContext);

        var result = await handler.Handle(
            CashCommand("TR-CTR-REDEEM", "A1") with
            {
                CustomerUserId = customerContext.UserId,
                CustomerConfirmedForPoints = true,
                UseAllPoints = true
            },
            CancellationToken.None);

        result.TotalAmount.ShouldBe(5_000m);
        var booking = context.Set<Booking>().Single(b => b.Id == result.BookingId);
        booking.UserId.ShouldBe(customerContext.UserId);
        booking.PointsUsed.ShouldBe(5_000);
        booking.TotalAmount.ShouldBe(5_000m);
        context.Set<User>().Single(u => u.Id == customerContext.UserId).PointBalance.ShouldBe(1_000);

        var payment = context.Set<Payment>().Single(p => p.BookingId == booking.Id);
        payment.Amount.ShouldBe(5_000m);
        var transaction = context.Set<PointTransaction>()
            .Single(t => t.BookingId == booking.Id && t.TransactionType == PointTransactionTypes.Redeem);
        transaction.Points.ShouldBe(-5_000);
        transaction.BalanceAfter.ShouldBe(1_000);
    }

    [Test]
    public async Task CounterSaleRequiresStaffConfirmationBeforeLinkingCustomerPoints()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var staffContext = await SeatFlowTestData.SeedStaffAsync(context);
        var customerContext = await SeatFlowTestData.SeedCustomerAsync(context);
        await SeedTripAsync(context, "TR-CTR-CONFIRM", TripStatus.Scheduled);
        var handler = CreateHandler(context, staffContext);

        var exception = await Should.ThrowAsync<ValidationException>(() =>
            handler.Handle(
                CashCommand("TR-CTR-CONFIRM", "A1") with { CustomerUserId = customerContext.UserId },
                CancellationToken.None));

        exception.Errors.SelectMany(x => x.Value)
            .ShouldContain(m => m.Contains("Staff phải xác nhận đúng tài khoản"));
    }

    [Test]
    public async Task CounterSaleRejectsConfirmationWithoutSelectedCustomer()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var staffContext = await SeatFlowTestData.SeedStaffAsync(context);
        await SeedTripAsync(context, "TR-CTR-NO-CUSTOMER", TripStatus.Scheduled);
        var handler = CreateHandler(context, staffContext);

        var exception = await Should.ThrowAsync<ValidationException>(() =>
            handler.Handle(
                CashCommand("TR-CTR-NO-CUSTOMER", "A1") with { CustomerConfirmedForPoints = true },
                CancellationToken.None));

        exception.Errors.SelectMany(x => x.Value)
            .ShouldContain(m => m.Contains("chưa chọn tài khoản khách hàng"));
    }

    [Test]
    public async Task CounterSaleRejectsPointUseWithoutSelectedCustomer()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var staffContext = await SeatFlowTestData.SeedStaffAsync(context);
        await SeedTripAsync(context, "TR-CTR-POINT-NO-CUSTOMER", TripStatus.Scheduled);
        var handler = CreateHandler(context, staffContext);

        var exception = await Should.ThrowAsync<ValidationException>(() =>
            handler.Handle(
                CashCommand("TR-CTR-POINT-NO-CUSTOMER", "A1") with { UseAllPoints = true },
                CancellationToken.None));

        exception.Errors.SelectMany(x => x.Value)
            .ShouldContain(m => m.Contains("Chỉ dùng điểm khi đã chọn"));
    }

    [Test]
    public async Task CounterSaleRejectsLinkedAccountThatIsNotActiveCustomer()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var staffContext = await SeatFlowTestData.SeedStaffAsync(context);
        var managerContext = await SeatFlowTestData.SeedManagerAsync(context);
        await SeedTripAsync(context, "TR-CTR-BAD-CUSTOMER", TripStatus.Scheduled);
        var handler = CreateHandler(context, staffContext);

        var exception = await Should.ThrowAsync<ValidationException>(() =>
            handler.Handle(
                CashCommand("TR-CTR-BAD-CUSTOMER", "A1") with
                {
                    CustomerUserId = managerContext.UserId,
                    CustomerConfirmedForPoints = true
                },
                CancellationToken.None));

        exception.Errors.SelectMany(x => x.Value)
            .ShouldContain(m => m.Contains("Tài khoản khách hàng không hợp lệ"));
    }

    [TestCase(TripStatus.Boarding)]
    [TestCase(TripStatus.InProgress)]
    [TestCase(TripStatus.Delayed)]
    public async Task StaffCanSellAfterDeparture(TripStatus tripStatus)
    {
        await using var context = SeatFlowTestData.CreateContext();
        var staffContext = await SeatFlowTestData.SeedStaffAsync(context);
        // Tàu đã rời bến 10 phút trước — khách lên ở bến giữa tuyến.
        await SeedTripAsync(context, "TR-CTR-2", tripStatus, departureTime: Now.AddMinutes(-10));
        var handler = CreateHandler(context, staffContext);

        var result = await handler.Handle(CashCommand("TR-CTR-2", "A1"), CancellationToken.None);

        result.BookingStatus.ShouldBe(nameof(BookingStatus.Confirmed));
    }

    [Test]
    public async Task CounterSaleRejectsReturnTripBeforeOutboundArrival()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var staffContext = await SeatFlowTestData.SeedStaffAsync(context);
        await SeedTripAsync(context, "TR-CTR-OUT-LATE", TripStatus.Scheduled, departureTime: Now.AddHours(12));
        await SeedTripAsync(context, "TR-CTR-RET-EARLY", TripStatus.Scheduled, departureTime: Now.AddHours(10));
        var handler = CreateHandler(context, staffContext);

        var exception = await Should.ThrowAsync<ValidationException>(() =>
            handler.Handle(
                CashCommand("TR-CTR-OUT-LATE", "A1") with
                {
                    ReturnTripCode = "TR-CTR-RET-EARLY",
                    ReturnItems = [Adult("A1")]
                },
                CancellationToken.None));

        exception.Errors.SelectMany(x => x.Value)
            .ShouldContain(m => m.Contains("Giờ chiều về phải sau giờ khách xuống ở chiều đi"));
    }

    [TestCase(TripStatus.Completed)]
    [TestCase(TripStatus.Cancelled)]
    public async Task StaffCannotSellForFinishedOrCancelledTrip(TripStatus tripStatus)
    {
        await using var context = SeatFlowTestData.CreateContext();
        var staffContext = await SeatFlowTestData.SeedStaffAsync(context);
        await SeedTripAsync(context, "TR-CTR-3", tripStatus, departureTime: Now.AddMinutes(-10));
        var handler = CreateHandler(context, staffContext);

        var exception = await Should.ThrowAsync<ValidationException>(() =>
            handler.Handle(CashCommand("TR-CTR-3", "A1"), CancellationToken.None));

        exception.Errors.SelectMany(x => x.Value)
            .ShouldContain(m => m.Contains("đã kết thúc hoặc đã hủy"));
    }

    [Test]
    public async Task CustomerBookingStillBlockedAfterCutoff()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var customerContext = await SeatFlowTestData.SeedCustomerAsync(context);
        // Cùng chuyến đã rời bến: khách tự đặt online vẫn phải bị chặn — chỉ quầy mới bán được.
        await SeedTripAsync(context, "TR-CTR-4", TripStatus.InProgress, departureTime: Now.AddMinutes(-10));

        var handler = new CreateBookingCommandHandler(
            context,
            customerContext,
            new SequentialBookingCodeGenerator(),
            new FixedFareCalculator(10000m),
            new FixedTimeProvider(Now));

        await Should.ThrowAsync<ValidationException>(() => handler.Handle(
            new CreateBookingCommand("TR-CTR-4", [Adult("A1")], null),
            CancellationToken.None));
    }

    [Test]
    public async Task CustomerBookingRequiresAccountEmailForSharedTicketEmail()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var customerContext = await SeatFlowTestData.SeedCustomerAsync(context);
        var customer = context.Users.Single(x => x.Id == customerContext.UserId);
        customer.Email = null;
        await context.SaveChangesAsync();

        var handler = new CreateBookingCommandHandler(
            context,
            customerContext,
            new SequentialBookingCodeGenerator(),
            new FixedFareCalculator(10000m),
            new FixedTimeProvider(Now));

        var exception = await Should.ThrowAsync<ValidationException>(() => handler.Handle(
            new CreateBookingCommand("TR-ANY", [Adult("A1")], null),
            CancellationToken.None));

        exception.Errors.SelectMany(x => x.Value)
            .ShouldContain(m => m.Contains("email liên hệ", StringComparison.OrdinalIgnoreCase));
    }

    [Test]
    public void CounterBookingAllowsMissingContactEmailForPrintedTicket()
    {
        var validator = new CreateCounterBookingCommandValidator();

        validator.Validate(CashCommand("TR-CTR-EMAIL", "A1") with { ContactEmail = null })
            .IsValid
            .ShouldBeTrue();
        validator.Validate(CashCommand("TR-CTR-EMAIL", "A1") with { ContactEmail = "" })
            .IsValid
            .ShouldBeTrue();
        validator.Validate(CashCommand("TR-CTR-EMAIL", "A1") with { ContactEmail = "not-an-email" })
            .Errors
            .ShouldContain(x => x.PropertyName == nameof(CreateCounterBookingCommand.ContactEmail));
    }

    [Test]
    public async Task CounterSaleWithoutContactEmailIssuesPrintableTicketsWithoutSendingEmail()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var staffContext = await SeatFlowTestData.SeedStaffAsync(context);
        await SeedTripAsync(context, "TR-CTR-PRINT", TripStatus.Scheduled);
        var notifications = new RecordingPaymentNotificationSender();
        var handler = CreateHandler(context, staffContext, notifications);

        var result = await handler.Handle(
            CashCommand("TR-CTR-PRINT", "A1") with { ContactEmail = null },
            CancellationToken.None);

        result.BookingStatus.ShouldBe(nameof(BookingStatus.Confirmed));
        result.PaymentStatus.ShouldBe("Paid");
        result.Manifest.ShouldNotBeNull();
        result.Manifest!.BookingQrToken.ShouldNotBeNullOrWhiteSpace();
        result.Manifest.Passengers.Single().TicketCode.ShouldNotBeNullOrWhiteSpace();
        result.Manifest.Passengers.Single().QrToken.ShouldNotBeNullOrWhiteSpace();
        notifications.ETickets.ShouldBeEmpty();

        var booking = context.Set<Booking>().Single(b => b.Id == result.BookingId);
        booking.ContactEmail.ShouldBeNull();
    }

    [Test]
    public async Task NonStaffCannotSellAtCounter()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var customerContext = await SeatFlowTestData.SeedCustomerAsync(context);
        await SeedTripAsync(context, "TR-CTR-5", TripStatus.Scheduled);
        var handler = CreateHandler(context, customerContext);

        await Should.ThrowAsync<ForbiddenAccessException>(() =>
            handler.Handle(CashCommand("TR-CTR-5", "A1"), CancellationToken.None));
    }

    [Test]
    public async Task CounterSaleRejectsSeatAlreadySoldOnOverlappingSegment()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var staffContext = await SeatFlowTestData.SeedStaffAsync(context);
        await SeedTripAsync(context, "TR-CTR-6", TripStatus.Boarding);
        var handler = CreateHandler(context, staffContext);

        await handler.Handle(CashCommand("TR-CTR-6", "A1"), CancellationToken.None);

        var exception = await Should.ThrowAsync<ValidationException>(() =>
            handler.Handle(CashCommand("TR-CTR-6", "A1"), CancellationToken.None));
        exception.Errors.SelectMany(x => x.Value).ShouldContain(m => m.Contains("already booked"));
    }

    [Test]
    public async Task PayOsSaleLeavesBookingPendingWithCheckoutLink()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var staffContext = await SeatFlowTestData.SeedStaffAsync(context);
        await SeedTripAsync(context, "TR-CTR-7", TripStatus.Scheduled);
        var handler = CreateHandler(context, staffContext);

        var result = await handler.Handle(
            CashCommand("TR-CTR-7", "A1") with { PaymentMethod = CounterPaymentMethod.PayOs },
            CancellationToken.None);

        result.PaymentMethod.ShouldBe(nameof(CounterPaymentMethod.PayOs));
        result.BookingStatus.ShouldBe(nameof(BookingStatus.PendingPayment));
        result.CheckoutUrl.ShouldNotBeNullOrWhiteSpace();
        // Chưa trả tiền thì chưa có vé, và ghế chỉ được giữ tới hạn hold.
        context.Set<Ticket>().Count(t => t.BookingId == result.BookingId).ShouldBe(0);
        result.HoldExpiresAt.ShouldBe(Now.Add(BookingSeatOccupancySupport.BookingHoldDuration));
    }

    [Test]
    public async Task FreeTicketOnlySaleIsSettledAtCounterEvenWhenPayOsRequested()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var staffContext = await SeatFlowTestData.SeedStaffAsync(context);
        await SeedTripAsync(context, "TR-CTR-8", TripStatus.Scheduled);
        var handler = CreateHandler(context, staffContext);

        // Vé SENIOR miễn phí → đơn 0đ, không qua được cổng thanh toán.
        var command = CashCommand("TR-CTR-8", "A1") with
        {
            Items = [Adult("A1") with { TicketTypeCode = "SENIOR", BirthYear = 1940 }],
            PaymentMethod = CounterPaymentMethod.PayOs
        };

        var result = await handler.Handle(command, CancellationToken.None);

        result.TotalAmount.ShouldBe(0m);
        result.BookingStatus.ShouldBe(nameof(BookingStatus.Confirmed));
        result.PaymentMethod.ShouldBe("Cash");
        context.Set<Ticket>().Count(t => t.BookingId == result.BookingId).ShouldBe(1);
    }

    private static CreateCounterBookingCommand CashCommand(string tripCode, string seat) =>
        new(
            tripCode,
            [Adult(seat)],
            "Khach Vang Lai",
            "0909000111",
            "khach@example.test");

    private static BookingItemRequest Adult(string seat) =>
        new(seat, "ADULT", "BB", "LT", "Khach Vang Lai", null, null, null, null, null);

    private static CreateCounterBookingCommandHandler CreateHandler(
        ApplicationDbContext context,
        TestUserContext userContext,
        IPaymentNotificationSender? paymentNotificationSender = null) =>
        new(
            context,
            userContext,
            new SequentialBookingCodeGenerator(),
            new FixedFareCalculator(10000m),
            new FixedTimeProvider(Now),
            new StubSender(context, userContext),
            paymentNotificationSender ?? new NoopPaymentNotificationSender());

    /// <summary>Trip Regular BB → LT, tàu 1 ghế STANDARD (A1).</summary>
    private static async Task SeedTripAsync(
        ApplicationDbContext context,
        string tripCode,
        TripStatus tripStatus,
        DateTimeOffset? departureTime = null)
    {
        var bb = await GetOrCreateStationAsync(context, "BB");
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
            Route = route, Station = lt, StationId = lt.Id, StopOrder = 2, DistanceFromPreviousKm = 3m
        });

        var boat = SeatFlowTestData.Boat(SeatSetupType.FullStandard, seatsConfigured: true, BoatStatus.Active);
        boat.SeatCount = 1;
        var seat = new Seat { Boat = boat, BoatId = boat.Id, Code = "A1", Deck = 1, Row = "A", Column = 1 };

        var trip = new Trip
        {
            Route = route,
            RouteId = route.Id,
            Boat = boat,
            BoatId = boat.Id,
            TripCode = tripCode,
            TripType = TripTypes.Regular,
            OperatingDate = DateOnly.FromDateTime(Now.UtcDateTime),
            DepartureTime = departureTime ?? Now.AddHours(2),
            ArrivalTime = (departureTime ?? Now.AddHours(2)).AddHours(1),
            CapacitySnapshot = 1,
            TripStatus = tripStatus
        };
        var tripSeat = new TripSeat { Trip = trip, TripId = trip.Id, Seat = seat, SeatId = seat.Id, Price = 10000m };

        context.AddRange(route, boat, seat, trip, tripSeat);
        await context.SaveChangesAsync();
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

    /// <summary>Chỉ phục vụ nhánh PayOS: chạy thẳng CreatePaymentCommandHandler với gateway giả.</summary>
    private sealed class StubSender : ISender
    {
        private readonly ApplicationDbContext _context;
        private readonly IUserContext _userContext;

        public StubSender(ApplicationDbContext context, IUserContext userContext)
        {
            _context = context;
            _userContext = userContext;
        }

        public async Task<TResponse> Send<TResponse>(
            IRequest<TResponse> request,
            CancellationToken cancellationToken = default)
        {
            if (request is CreatePaymentCommand createPayment)
            {
                var handler = new CreatePaymentCommandHandler(
                    _context,
                    _userContext,
                    new StubPaymentGateway(),
                    new NoopPaymentNotificationSender(),
                    new FixedTimeProvider(Now));
                return (TResponse)(object)await handler.Handle(createPayment, cancellationToken);
            }

            throw new NotSupportedException($"Unexpected request {request.GetType().Name}.");
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest => throw new NotSupportedException();

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(
            object request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class SequentialBookingCodeGenerator : IBookingCodeGenerator
    {
        private int _next;

        public Task<string> GenerateAsync(CancellationToken cancellationToken) =>
            Task.FromResult($"BK-CTR-{Interlocked.Increment(ref _next):D4}");
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

    private sealed class StubPaymentGateway : ICharterBookingPaymentGateway
    {
        public Task<CharterBookingDepositPaymentResult> CreateDepositPaymentAsync(
            CharterBookingDepositPaymentRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new CharterBookingDepositPaymentResult(
                request.OrderCode.ToString(),
                $"https://pay.test/{request.OrderCode}",
                $"QR-{request.OrderCode}",
                "PENDING"));

        public Task<CharterBookingPaymentStatusResult> GetPaymentAsync(
            long orderCode,
            CancellationToken cancellationToken) =>
            Task.FromResult(new CharterBookingPaymentStatusResult(
                orderCode, null, "PENDING", orderCode.ToString()));

        public Task<CharterBookingRefundPayoutResult> CreateRefundPayoutAsync(
            CharterBookingRefundPayoutRequest request,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<CharterBookingPaymentCancellationResult> CancelPaymentAsync(
            long orderCode,
            string reason,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<CharterBookingRefundPayoutResult?> GetRefundPayoutByReferenceIdAsync(
            string referenceId,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public bool IsValidWebhook(CharterBookingDepositPaymentWebhook webhook) => true;
    }

    private sealed class NoopPaymentNotificationSender : IPaymentNotificationSender
    {
        public Task SendPaymentSucceededAsync(
            PaymentSucceededNotification notification,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task SendBoardingPassAsync(
            BoardingPassNotification notification,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task SendETicketsAsync(
            ETicketNotification notification,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task SendRefundReleasedAsync(
            RefundReleasedNotification notification,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task SendCharterETicketsAsync(
            ETicketNotification notification,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class RecordingPaymentNotificationSender : IPaymentNotificationSender
    {
        public List<PaymentSucceededNotification> PaymentSucceeded { get; } = [];
        public List<BoardingPassNotification> BoardingPasses { get; } = [];
        public List<ETicketNotification> ETickets { get; } = [];

        public Task SendPaymentSucceededAsync(
            PaymentSucceededNotification notification,
            CancellationToken cancellationToken)
        {
            PaymentSucceeded.Add(notification);
            return Task.CompletedTask;
        }

        public Task SendBoardingPassAsync(
            BoardingPassNotification notification,
            CancellationToken cancellationToken)
        {
            BoardingPasses.Add(notification);
            return Task.CompletedTask;
        }

        public Task SendETicketsAsync(
            ETicketNotification notification,
            CancellationToken cancellationToken)
        {
            ETickets.Add(notification);
            return Task.CompletedTask;
        }

        public Task SendRefundReleasedAsync(
            RefundReleasedNotification notification,
            CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task SendCharterETicketsAsync(
            ETicketNotification notification,
            CancellationToken cancellationToken)
        {
            ETickets.Add(notification);
            return Task.CompletedTask;
        }
    }
}
