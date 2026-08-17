using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using SaigonWaterbus.Application.Common.Exceptions;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Application.Payments;
using SaigonWaterbus.Application.UnitTests.TestInfrastructure;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using Shouldly;

namespace SaigonWaterbus.Application.UnitTests.Payments;

public class CreatePaymentCommandTests
{
    [Test]
    public async Task CharterBookingDepositCreatesNewPayment()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userId = Guid.NewGuid();
        var now = new DateTimeOffset(2026, 7, 7, 0, 0, 0, TimeSpan.Zero);
        var booking = new Booking
        {
            BookingType = Booking.CharterBookingType,
            UserId = userId,
            BookingCode = "CB-PAYMENT",
            ContactName = "Nguyen Van A",
            ContactPhone = "0900000000",
            BookingStatus = BookingStatus.Quoted,
            PaymentStatus = "Unpaid",
            DepartureDate = new DateOnly(2030, 1, 1),
            RentalUnit = BoatRentalUnit.Day,
            DurationValue = 1,
            AdultCount = 1,
            PassengerCount = 1,
            SubtotalAmount = 10000,
            TotalAmount = 10000,
            RemainingAmount = 10000,
            HoldExpiresAt = now.AddHours(1)
        };
        context.Add(booking);
        await context.SaveChangesAsync();

        var handler = new CreatePaymentCommandHandler(
            context,
            new TestUserContext(userId),
            new TestPaymentGateway(),
            new TestPaymentNotificationSender(),
            new FixedTimeProvider(now));

        var result = await handler.Handle(
            new CreatePaymentCommand(booking.Id, BookingPaymentOption.Deposit),
            CancellationToken.None);

        result.Amount.ShouldBe(5000);
        result.PaymentStatus.ShouldBe("Pending");
        result.CheckoutUrl.ShouldBe("https://example.test/checkout");
        result.ExpiresAt.ShouldBe(now.AddMinutes(5));
        booking.HoldExpiresAt.ShouldBe(now.AddHours(12));
        context.Set<Payment>().Count().ShouldBe(1);
        context.Set<Payment>().Single().PaymentPurpose.ShouldBe("Deposit");
        context.Set<Payment>().Single().ExpiresAt.ShouldBe(now.AddMinutes(5));
    }

    [Test]
    public async Task ExpiredPendingPaymentCreatesNewPaymentLink()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userId = Guid.NewGuid();
        var now = new DateTimeOffset(2026, 7, 7, 0, 0, 0, TimeSpan.Zero);
        var booking = new Booking
        {
            UserId = userId,
            BookingCode = "BK-EXPIRED-LINK",
            ContactName = "Nguyen Van A",
            ContactPhone = "0900000000",
            BookingStatus = BookingStatus.PendingPayment,
            PaymentStatus = "Unpaid",
            SubtotalAmount = 10000,
            TotalAmount = 10000,
            RemainingAmount = 10000
        };
        var expiredPayment = new Payment
        {
            Booking = booking,
            PaymentCode = "1000005",
            Provider = "PayOS",
            Amount = 10000,
            Currency = "VND",
            PaymentMethod = "PayOS",
            PaymentPurpose = "Full",
            PaymentStatus = "Pending",
            CheckoutUrl = "https://example.test/old-checkout",
            ExpiresAt = now.AddSeconds(-1)
        };
        context.AddRange(booking, expiredPayment);
        await context.SaveChangesAsync();
        var gateway = new TestPaymentGateway();

        var handler = new CreatePaymentCommandHandler(
            context,
            new TestUserContext(userId),
            gateway,
            new TestPaymentNotificationSender(),
            new FixedTimeProvider(now));

        var result = await handler.Handle(
            new CreatePaymentCommand(booking.Id),
            CancellationToken.None);

        expiredPayment.PaymentStatus.ShouldBe("Expired");
        result.PaymentId.ShouldNotBe(expiredPayment.Id);
        result.PaymentStatus.ShouldBe("Pending");
        result.ExpiresAt.ShouldBe(now.AddMinutes(5));
        gateway.CreateRequests.Count.ShouldBe(1);
        context.Set<Payment>().Count().ShouldBe(2);
    }

    [Test]
    public async Task RegularBookingPaymentLinkExpiresAtHoldDeadlineWhenHoldIsEarlier()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userId = Guid.NewGuid();
        var now = new DateTimeOffset(2026, 7, 7, 0, 0, 0, TimeSpan.Zero);
        var holdExpiresAt = now.AddMinutes(2);
        var booking = new Booking
        {
            UserId = userId,
            BookingCode = "BK-HOLD-LIMIT",
            ContactName = "Nguyen Van A",
            ContactPhone = "0900000000",
            BookingStatus = BookingStatus.PendingPayment,
            PaymentStatus = "Unpaid",
            SubtotalAmount = 10000,
            TotalAmount = 10000,
            RemainingAmount = 10000,
            HoldExpiresAt = holdExpiresAt
        };
        context.Add(booking);
        await context.SaveChangesAsync();
        var gateway = new TestPaymentGateway();

        var handler = new CreatePaymentCommandHandler(
            context,
            new TestUserContext(userId),
            gateway,
            new TestPaymentNotificationSender(),
            new FixedTimeProvider(now));

        var result = await handler.Handle(new CreatePaymentCommand(booking.Id), CancellationToken.None);

        result.ExpiresAt.ShouldBe(holdExpiresAt);
        gateway.CreateRequests.Single().ExpiredAt.ShouldBe(holdExpiresAt);
        context.Set<Payment>().Single().ExpiresAt.ShouldBe(holdExpiresAt);
    }

    [Test]
    public async Task RegularZeroAmountBookingCompletesWithoutPayOsAndIssuesTicket()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userContext = await SeatFlowTestData.SeedCustomerAsync(context);
        var userId = userContext.UserId!.Value;
        var now = new DateTimeOffset(2026, 7, 7, 0, 0, 0, TimeSpan.Zero);
        var booking = new Booking
        {
            UserId = userId,
            BookingCode = "BK-FREE",
            ContactName = "Nguyen Van A",
            ContactPhone = "0900000000",
            BookingStatus = BookingStatus.PendingPayment,
            PaymentStatus = "Unpaid",
            SubtotalAmount = 0,
            TotalAmount = 0,
            RemainingAmount = 0,
            HoldExpiresAt = now.AddMinutes(15)
        };
        var passenger = new BookingPassenger
        {
            Booking = booking,
            FullName = "Nguyen Van A",
            PassengerType = "INFANT",
            UnitPrice = 0
        };
        context.AddRange(booking, passenger);
        await context.SaveChangesAsync();
        var gateway = new TestPaymentGateway();

        var handler = new CreatePaymentCommandHandler(
            context,
            new TestUserContext(userId),
            gateway,
            new TestPaymentNotificationSender(),
            new FixedTimeProvider(now));

        var result = await handler.Handle(new CreatePaymentCommand(booking.Id), CancellationToken.None);

        result.Amount.ShouldBe(0);
        result.Provider.ShouldBe("System");
        result.PaymentMethod.ShouldBe("Free");
        result.PaymentStatus.ShouldBe("Paid");
        result.BookingStatus.ShouldBe(BookingStatus.Confirmed.ToString());
        result.BookingPaymentStatus.ShouldBe("Paid");
        result.BookingRemainingAmount.ShouldBe(0);
        result.CheckoutUrl.ShouldBeNull();
        result.PaidAt.ShouldBe(now);
        gateway.CreateRequests.ShouldBeEmpty();

        var payment = context.Set<Payment>().Single();
        payment.Amount.ShouldBe(0);
        payment.Provider.ShouldBe("System");
        payment.PaymentMethod.ShouldBe("Free");
        payment.PaymentStatus.ShouldBe("Paid");
        payment.PaidAt.ShouldBe(now);
        booking.BookingStatus.ShouldBe(BookingStatus.Confirmed);
        booking.PaymentStatus.ShouldBe("Paid");
        context.Tickets.Count().ShouldBe(1);
        context.Tickets.Single().BookingPassengerId.ShouldBe(passenger.Id);
    }

    [Test]
    public async Task CharterZeroAmountBookingStillRequiresPositivePaymentAmount()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userId = Guid.NewGuid();
        var now = new DateTimeOffset(2026, 7, 7, 0, 0, 0, TimeSpan.Zero);
        var booking = new Booking
        {
            BookingType = Booking.CharterBookingType,
            UserId = userId,
            BookingCode = "CB-FREE-REJECT",
            ContactName = "Nguyen Van A",
            ContactPhone = "0900000000",
            BookingStatus = BookingStatus.Quoted,
            PaymentStatus = "Unpaid",
            DepartureDate = new DateOnly(2030, 1, 1),
            RentalUnit = BoatRentalUnit.Day,
            DurationValue = 1,
            AdultCount = 1,
            PassengerCount = 1,
            SubtotalAmount = 0,
            TotalAmount = 0,
            RemainingAmount = 0,
            HoldExpiresAt = now.AddHours(1)
        };
        context.Add(booking);
        await context.SaveChangesAsync();
        var gateway = new TestPaymentGateway();

        var handler = new CreatePaymentCommandHandler(
            context,
            new TestUserContext(userId),
            gateway,
            new TestPaymentNotificationSender(),
            new FixedTimeProvider(now));

        var exception = await Should.ThrowAsync<ValidationException>(() =>
            handler.Handle(new CreatePaymentCommand(booking.Id), CancellationToken.None));

        exception.Errors.SelectMany(x => x.Value)
            .ShouldContain("Booking chưa có số tiền cần thanh toán.");
        gateway.CreateRequests.ShouldBeEmpty();
        context.Set<Payment>().Count().ShouldBe(0);
    }

    [Test]
    public async Task RegularBookingAppliesPromotionWhenCreatingPayment()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userId = Guid.NewGuid();
        var promotion = new Promotion
        {
            PromotionCode = "WELCOME10",
            PromotionName = "Welcome",
            PromotionType = PromotionType.Percent,
            DiscountValue = 10,
            ValidFrom = DateTimeOffset.UtcNow.AddDays(-1),
            ValidTo = DateTimeOffset.UtcNow.AddDays(1),
            Status = PromotionStatus.Active
        };
        var booking = new Booking
        {
            UserId = userId,
            BookingCode = "BK-CHECKOUT-PROMO",
            ContactName = "Nguyen Van A",
            ContactPhone = "0900000000",
            BookingStatus = BookingStatus.PendingPayment,
            PaymentStatus = "Unpaid",
            SubtotalAmount = 10000,
            TotalAmount = 10000,
            RemainingAmount = 10000
        };
        context.AddRange(promotion, booking);
        await context.SaveChangesAsync();

        var handler = new CreatePaymentCommandHandler(
            context,
            new TestUserContext(userId),
            new TestPaymentGateway(),
            new TestPaymentNotificationSender(),
            TimeProvider.System);

        var result = await handler.Handle(
            new CreatePaymentCommand(booking.Id, PromotionCode: "welcome10"),
            CancellationToken.None);

        result.Amount.ShouldBe(9000);
        booking.PromotionId.ShouldBe(promotion.Id);
        booking.DiscountAmount.ShouldBe(1000);
        booking.TotalAmount.ShouldBe(9000);
        booking.RemainingAmount.ShouldBe(0);
        // Lượt dùng suy ra từ bookings: đúng 1 booking active đang dùng mã.
        CountActivePromotionUsage(context, promotion.Id).ShouldBe(1);
        context.Set<Payment>().Single().Amount.ShouldBe(9000);
    }

    [Test]
    public async Task CreatingPaymentRejectsPromotionChangeWhenPendingPaymentExists()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userId = Guid.NewGuid();
        var promotion = new Promotion
        {
            PromotionCode = "WELCOME10",
            PromotionName = "Welcome",
            PromotionType = PromotionType.Percent,
            DiscountValue = 10,
            ValidFrom = DateTimeOffset.UtcNow.AddDays(-1),
            ValidTo = DateTimeOffset.UtcNow.AddDays(1),
            Status = PromotionStatus.Active
        };
        var booking = new Booking
        {
            UserId = userId,
            BookingCode = "BK-PENDING-PROMO",
            ContactName = "Nguyen Van A",
            ContactPhone = "0900000000",
            BookingStatus = BookingStatus.PendingPayment,
            PaymentStatus = "Unpaid",
            SubtotalAmount = 10000,
            TotalAmount = 10000,
            RemainingAmount = 10000
        };
        var payment = new Payment
        {
            Booking = booking,
            PaymentCode = "1000004",
            Provider = "PayOS",
            Amount = 10000,
            Currency = "VND",
            PaymentMethod = "PayOS",
            PaymentPurpose = "Full",
            PaymentStatus = "Pending",
            CheckoutUrl = "https://example.test/checkout"
        };
        context.AddRange(promotion, booking, payment);
        await context.SaveChangesAsync();

        var handler = new CreatePaymentCommandHandler(
            context,
            new TestUserContext(userId),
            new TestPaymentGateway(),
            new TestPaymentNotificationSender(),
            TimeProvider.System);

        var exception = await Should.ThrowAsync<ValidationException>(() =>
            handler.Handle(
                new CreatePaymentCommand(booking.Id, PromotionCode: "WELCOME10"),
                CancellationToken.None));

        exception.Errors["promotionCode"]
            .ShouldContain("Không thể đổi mã giảm giá khi booking đã có payment đang chờ hoặc đã thanh toán.");
        booking.TotalAmount.ShouldBe(10000);
        // Mã bị từ chối nên không booking nào dùng nó.
        CountActivePromotionUsage(context, promotion.Id).ShouldBe(0);
    }

    [Test]
    public async Task CharterBookingPaymentGatewayFailureRestoresUnpaidAmounts()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userId = Guid.NewGuid();
        var booking = new Booking
        {
            BookingType = Booking.CharterBookingType,
            UserId = userId,
            BookingCode = "CB-PAYMENT-FAIL",
            ContactName = "Nguyen Van A",
            ContactPhone = "0900000000",
            BookingStatus = BookingStatus.Quoted,
            PaymentStatus = "Unpaid",
            DepartureDate = new DateOnly(2030, 1, 1),
            RentalUnit = BoatRentalUnit.Day,
            DurationValue = 1,
            AdultCount = 1,
            PassengerCount = 1,
            SubtotalAmount = 10000,
            TotalAmount = 10000,
            RemainingAmount = 10000
        };
        context.Add(booking);
        await context.SaveChangesAsync();

        var gatewayFailure = new PaymentGatewayException("PayOS failed");
        var handler = new CreatePaymentCommandHandler(
            context,
            new TestUserContext(userId),
            new TestPaymentGateway(gatewayFailure, gatewayFailure),
            new TestPaymentNotificationSender(),
            TimeProvider.System);

        await Should.ThrowAsync<ValidationException>(() =>
            handler.Handle(
                new CreatePaymentCommand(booking.Id, BookingPaymentOption.Full),
                CancellationToken.None));

        booking.PaymentStatus.ShouldBe("Failed");
        booking.DepositAmount.ShouldBe(0);
        booking.RemainingAmount.ShouldBe(10000);
        context.Set<Payment>().Count().ShouldBe(1);
        context.Set<Payment>().Single().PaymentStatus.ShouldBe("Failed");
    }

    [Test]
    public async Task CharterBookingRemainingPaymentKeepsDepositPaidSummaryWhilePending()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userId = Guid.NewGuid();
        var booking = new Booking
        {
            BookingType = Booking.CharterBookingType,
            UserId = userId,
            BookingCode = "CB-REMAINING",
            ContactName = "Nguyen Van A",
            ContactPhone = "0900000000",
            BookingStatus = BookingStatus.Confirmed,
            PaymentStatus = "DepositPaid",
            DepartureDate = new DateOnly(2030, 1, 1),
            RentalUnit = BoatRentalUnit.Day,
            DurationValue = 1,
            AdultCount = 1,
            PassengerCount = 1,
            SubtotalAmount = 10000,
            TotalAmount = 10000,
            DepositAmount = 5000,
            RemainingAmount = 5000
        };
        var depositPayment = new Payment
        {
            Booking = booking,
            PaymentCode = "1000000",
            Provider = "PayOS",
            Amount = 5000,
            Currency = "VND",
            PaymentMethod = "PayOS",
            PaymentPurpose = "Deposit",
            PaymentStatus = "Paid",
            PaidAt = DateTimeOffset.UtcNow
        };
        context.AddRange(booking, depositPayment);
        await context.SaveChangesAsync();

        var handler = new CreatePaymentCommandHandler(
            context,
            new TestUserContext(userId),
            new TestPaymentGateway(),
            new TestPaymentNotificationSender(),
            TimeProvider.System);

        var result = await handler.Handle(
            new CreatePaymentCommand(booking.Id, BookingPaymentOption.Remaining),
            CancellationToken.None);

        result.Amount.ShouldBe(5000);
        result.PaymentPurpose.ShouldBe("Remaining");
        result.PaymentStatus.ShouldBe("Pending");
        result.BookingPaymentStatus.ShouldBe("DepositPaid");
        result.BookingDepositAmount.ShouldBe(5000);
        result.BookingRemainingAmount.ShouldBe(5000);
        booking.PaymentStatus.ShouldBe("DepositPaid");
        booking.DepositAmount.ShouldBe(5000);
        booking.RemainingAmount.ShouldBe(5000);

        var remainingPayment = context.Set<Payment>()
            .Where(x => x.PaymentPurpose == "Remaining")
            .ShouldHaveSingleItem();
        remainingPayment.Amount.ShouldBe(5000);
        remainingPayment.PaymentStatus.ShouldBe("Pending");
    }

    [Test]
    public async Task WebhookPaidDepositSendsPaymentNotification()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var booking = new Booking
        {
            BookingType = Booking.CharterBookingType,
            UserId = Guid.NewGuid(),
            BookingCode = "CB-DEPOSIT",
            ContactName = "Nguyen Van A",
            ContactPhone = "0900000000",
            ContactEmail = "customer@gmail.com",
            BookingStatus = BookingStatus.Quoted,
            PaymentStatus = "Unpaid",
            DepartureDate = new DateOnly(2030, 1, 1),
            RentalUnit = BoatRentalUnit.Day,
            DurationValue = 1,
            AdultCount = 1,
            PassengerCount = 1,
            SubtotalAmount = 10000,
            TotalAmount = 10000,
            DepositAmount = 5000,
            RemainingAmount = 5000
        };
        var payment = new Payment
        {
            Booking = booking,
            PaymentCode = "1000001",
            Provider = "PayOS",
            Amount = 5000,
            Currency = "VND",
            PaymentMethod = "PayOS",
            PaymentPurpose = "Deposit",
            PaymentStatus = "Pending"
        };
        context.AddRange(booking, payment);
        await context.SaveChangesAsync();
        var sender = new TestPaymentNotificationSender();
        var handler = new HandlePaymentWebhookCommandHandler(
            context,
            new TestPaymentGateway(),
            sender,
            TimeProvider.System);

        await handler.Handle(new HandlePaymentWebhookCommand(CreatePaidWebhook(1000001, 5000)), CancellationToken.None);

        sender.Notifications.Count.ShouldBe(1);
        sender.Notifications.Single().BookingCode.ShouldBe("CB-DEPOSIT");
        sender.Notifications.Single().IsFullyPaid.ShouldBeFalse();
        sender.Notifications.Single().PaymentPurpose.ShouldBe("Deposit");
    }

    [Test]
    public async Task WebhookPaidFullSendsPaymentNotification()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var booking = new Booking
        {
            BookingType = Booking.CharterBookingType,
            UserId = Guid.NewGuid(),
            BookingCode = "CB-FULL",
            ContactName = "Nguyen Van A",
            ContactPhone = "0900000000",
            ContactEmail = "customer@gmail.com",
            BookingStatus = BookingStatus.Quoted,
            PaymentStatus = "Unpaid",
            DepartureDate = new DateOnly(2030, 1, 1),
            RentalUnit = BoatRentalUnit.Day,
            DurationValue = 1,
            AdultCount = 1,
            PassengerCount = 1,
            SubtotalAmount = 10000,
            TotalAmount = 10000,
            RemainingAmount = 10000
        };
        var payment = new Payment
        {
            Booking = booking,
            PaymentCode = "1000002",
            Provider = "PayOS",
            Amount = 10000,
            Currency = "VND",
            PaymentMethod = "PayOS",
            PaymentPurpose = "Full",
            PaymentStatus = "Pending"
        };
        context.AddRange(booking, payment);
        await context.SaveChangesAsync();
        var sender = new TestPaymentNotificationSender();
        var handler = new HandlePaymentWebhookCommandHandler(
            context,
            new TestPaymentGateway(),
            sender,
            TimeProvider.System);

        await handler.Handle(new HandlePaymentWebhookCommand(CreatePaidWebhook(1000002, 10000)), CancellationToken.None);

        sender.Notifications.Count.ShouldBe(1);
        sender.Notifications.Single().BookingCode.ShouldBe("CB-FULL");
        sender.Notifications.Single().IsFullyPaid.ShouldBeTrue();
        sender.Notifications.Single().PaymentPurpose.ShouldBe("Full");
        sender.Notifications.Single().DepositAmount.ShouldBe(10000);
        sender.Notifications.Single().RemainingAmount.ShouldBe(0);
    }

    [Test]
    public async Task WebhookPaidCreatesInAppNotificationOnce()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userId = Guid.NewGuid();
        var booking = new Booking
        {
            BookingType = Booking.CharterBookingType,
            UserId = userId,
            BookingCode = "CB-NOTIF",
            ContactName = "Nguyen Van A",
            ContactPhone = "0900000000",
            ContactEmail = "customer@gmail.com",
            BookingStatus = BookingStatus.Quoted,
            PaymentStatus = "Unpaid",
            DepartureDate = new DateOnly(2030, 1, 1),
            RentalUnit = BoatRentalUnit.Day,
            DurationValue = 1,
            AdultCount = 1,
            PassengerCount = 1,
            SubtotalAmount = 10000,
            TotalAmount = 10000,
            DepositAmount = 5000,
            RemainingAmount = 5000
        };
        var payment = new Payment
        {
            Booking = booking,
            PaymentCode = "1000011",
            Provider = "PayOS",
            Amount = 5000,
            Currency = "VND",
            PaymentMethod = "PayOS",
            PaymentPurpose = "Deposit",
            PaymentStatus = "Pending"
        };
        context.AddRange(booking, payment);
        await context.SaveChangesAsync();
        var realtimeNotifier = new RecordingNotificationRealtimeNotifier();
        var handler = new HandlePaymentWebhookCommandHandler(
            context,
            new TestPaymentGateway(),
            new TestPaymentNotificationSender(),
            TimeProvider.System,
            notificationRealtimeNotifier: realtimeNotifier);

        await handler.Handle(new HandlePaymentWebhookCommand(CreatePaidWebhook(1000011, 5000)), CancellationToken.None);

        var notification = context.Set<Notification>().Single();
        notification.UserId.ShouldBe(userId);
        notification.Type.ShouldBe("booking_confirmed");
        notification.RelatedEntityType.ShouldBe("booking");
        notification.RelatedEntityId.ShouldBe(booking.Id);
        notification.IsRead.ShouldBeFalse();
        notification.Title.ShouldBe("Đã nhận tiền đặt cọc");
        notification.Body.ShouldNotBeNullOrWhiteSpace();
        realtimeNotifier.Published.Count.ShouldBe(1);
        realtimeNotifier.Published.Single().UserId.ShouldBe(userId);
        realtimeNotifier.Published.Single().NotificationId.ShouldBe(notification.Id);

        // Webhook PayOS có thể bắn lại — payment đã Paid thì không tạo thông báo trùng.
        await handler.Handle(new HandlePaymentWebhookCommand(CreatePaidWebhook(1000011, 5000)), CancellationToken.None);
        context.Set<Notification>().Count().ShouldBe(1);
        realtimeNotifier.Published.Count.ShouldBe(1);
    }

    [Test]
    public async Task WebhookPaidGuestBookingSkipsInAppNotification()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var booking = new Booking
        {
            BookingType = Booking.CharterBookingType,
            UserId = null,
            BookingCode = "CB-GUEST",
            ContactName = "Nguyen Van A",
            ContactPhone = "0900000000",
            ContactEmail = "customer@gmail.com",
            BookingStatus = BookingStatus.Quoted,
            PaymentStatus = "Unpaid",
            DepartureDate = new DateOnly(2030, 1, 1),
            RentalUnit = BoatRentalUnit.Day,
            DurationValue = 1,
            AdultCount = 1,
            PassengerCount = 1,
            SubtotalAmount = 10000,
            TotalAmount = 10000,
            RemainingAmount = 10000
        };
        var payment = new Payment
        {
            Booking = booking,
            PaymentCode = "1000012",
            Provider = "PayOS",
            Amount = 10000,
            Currency = "VND",
            PaymentMethod = "PayOS",
            PaymentPurpose = "Full",
            PaymentStatus = "Pending"
        };
        context.AddRange(booking, payment);
        await context.SaveChangesAsync();
        var sender = new TestPaymentNotificationSender();
        var handler = new HandlePaymentWebhookCommandHandler(
            context,
            new TestPaymentGateway(),
            sender,
            TimeProvider.System);

        await handler.Handle(new HandlePaymentWebhookCommand(CreatePaidWebhook(1000012, 10000)), CancellationToken.None);

        context.Set<Notification>().Count().ShouldBe(0);
        sender.Notifications.Count.ShouldBe(1);
    }

    [Test]
    public async Task WebhookPaidRegularBookingCreatesPassengerTicket()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var booking = new Booking
        {
            UserId = Guid.NewGuid(),
            BookingCode = "BK-FULL",
            ContactName = "Nguyen Van A",
            ContactPhone = "0900000000",
            ContactEmail = "customer@gmail.com",
            BookingStatus = BookingStatus.PendingPayment,
            PaymentStatus = "Unpaid",
            SubtotalAmount = 10000,
            TotalAmount = 10000,
            RemainingAmount = 10000
        };
        var passenger = new BookingPassenger
        {
            Booking = booking,
            FullName = "Nguyen Van A",
            PassengerType = "ADULT"
        };
        passenger.UnitPrice = 10000;
        var payment = new Payment
        {
            Booking = booking,
            PaymentCode = "1000003",
            Provider = "PayOS",
            Amount = 10000,
            Currency = "VND",
            PaymentMethod = "PayOS",
            PaymentPurpose = "Full",
            PaymentStatus = "Pending"
        };
        context.AddRange(booking, passenger, payment);
        await context.SaveChangesAsync();
        var sender = new TestPaymentNotificationSender();
        var handler = new HandlePaymentWebhookCommandHandler(
            context,
            new TestPaymentGateway(),
            sender,
            TimeProvider.System);

        await handler.Handle(new HandlePaymentWebhookCommand(CreatePaidWebhook(1000003, 10000)), CancellationToken.None);

        var ticket = context.Tickets.Single();
        ticket.BookingId.ShouldBe(booking.Id);
        ticket.BookingPassengerId.ShouldBe(passenger.Id);
        ticket.TicketStatus.ShouldBe(TicketStatus.Active);
        ticket.TicketCode.ShouldNotBeNullOrWhiteSpace();
        ticket.QrToken.ShouldNotBeNullOrWhiteSpace();
    }

    [Test]
    public async Task SyncPaymentByOrderCodeUsesPayOsOrderCode()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userId = Guid.NewGuid();
        var booking = new Booking
        {
            UserId = userId,
            BookingCode = "BK-SYNC-ORDER",
            ContactName = "Nguyen Van A",
            ContactPhone = "0900000000",
            BookingStatus = BookingStatus.PendingPayment,
            PaymentStatus = "Unpaid",
            SubtotalAmount = 10000,
            TotalAmount = 10000,
            RemainingAmount = 10000
        };
        var payment = new Payment
        {
            Booking = booking,
            PaymentCode = "123456",
            Provider = "PayOS",
            Amount = 10000,
            Currency = "VND",
            PaymentMethod = "PayOS",
            PaymentPurpose = "Full",
            PaymentStatus = "Pending"
        };
        context.AddRange(booking, payment);
        await context.SaveChangesAsync();

        var handler = new SyncPaymentCommandHandler(
            context,
            new TestUserContext(userId),
            new TestPaymentGateway(),
            new TestPaymentNotificationSender(),
            TimeProvider.System);

        var result = await handler.Handle(new SyncPaymentByOrderCodeCommand(123456), CancellationToken.None);

        result.PaymentId.ShouldBe(payment.Id);
        result.PaymentCode.ShouldBe("123456");
        result.CheckoutUrl.ShouldBe("https://example.test/checkout");
    }

    [Test]
    public async Task RefundPaymentFailureStoresSystemCalculatedAmount()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userId = Guid.NewGuid();
        var now = new DateTimeOffset(2026, 7, 7, 10, 0, 0, TimeSpan.Zero);
        var (booking, payment) = PaidCharterBooking(userId, "BK-REFUND-FAIL", now);
        payment.PaymentCode = "2000000";
        context.AddRange(booking, payment);
        await context.SaveChangesAsync();

        var (handler, otpChallenge) = await CreateRefundHandlerAsync(
            context,
            userId,
            payment,
            now,
            new TestPaymentGateway(refundException: new PaymentGatewayException("PayOS payout failed")));

        await Should.ThrowAsync<ValidationException>(() =>
            handler.Handle(
                CreateRefundCommand(payment, otpChallenge, "Customer refund"),
                CancellationToken.None));

        payment.RefundRequestedAmount.ShouldBe(10000);
        payment.RefundAmount.ShouldBe(0);
        payment.RefundMethod.ShouldBe("PayOS");
        payment.RefundReason.ShouldBe("Customer refund");
        payment.RefundStatus.ShouldBe("Failed");
        payment.RefundFailureReason.ShouldBe("PayOS payout failed");
        payment.RefundProcessedByUserId.ShouldBe(userId);
    }

    [Test]
    public async Task RequestRefundOtpDefaultsToVerifiedVietnamPhone()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userId = Guid.NewGuid();
        var now = new DateTimeOffset(2026, 7, 7, 10, 0, 0, TimeSpan.Zero);
        var user = SeedCustomer(
            context,
            userId,
            email: "nguyenvana@example.com",
            phoneNumber: "0901234567",
            phoneVerifiedAt: now.AddDays(-1));
        var (booking, payment) = PaidCharterBooking(user.Id, "BK-REFUND-OTP-PHONE", now);
        context.AddRange(booking, payment);
        await context.SaveChangesAsync();
        var emailSender = new TestOtpSender();
        var smsSender = new TestSmsOtpSender();
        var handler = CreateRequestRefundOtpHandler(context, user.Id, now, emailSender, smsSender);

        var result = await handler.Handle(new RequestRefundOtpCommand(payment.Id), CancellationToken.None);

        result.Channel.ShouldBe(OtpChannel.Phone);
        result.MaskedDestination.ShouldBe("masked-phone:+84901234567");
        smsSender.SentPhoneNumbers.ShouldBe(["+84901234567"]);
        emailSender.SentEmails.ShouldBeEmpty();

        var challenge = await context.Set<OtpChallenge>()
            .SingleAsync(x => x.UserId == user.Id && x.Purpose == OtpPurpose.Refund);
        challenge.Channel.ShouldBe(OtpChannel.Phone);
        challenge.Email.ShouldBe("+84901234567");
        challenge.PendingPhoneNumber.ShouldBe(payment.Id.ToString("N"));
        challenge.ExpiresAt.ShouldBe(now.AddMinutes(5));
        challenge.ResendAvailableAt.ShouldBe(now.AddSeconds(30));
    }

    [Test]
    public async Task RequestRefundOtpCanUseEmailWhenRequested()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userId = Guid.NewGuid();
        var now = new DateTimeOffset(2026, 7, 7, 10, 0, 0, TimeSpan.Zero);
        var user = SeedCustomer(
            context,
            userId,
            email: "nguyenvana@example.com",
            phoneNumber: "0901234567",
            phoneVerifiedAt: now.AddDays(-1));
        var (booking, payment) = PaidCharterBooking(user.Id, "BK-REFUND-OTP-EMAIL", now);
        context.AddRange(booking, payment);
        await context.SaveChangesAsync();
        var emailSender = new TestOtpSender();
        var smsSender = new TestSmsOtpSender();
        var handler = CreateRequestRefundOtpHandler(context, user.Id, now, emailSender, smsSender);

        var result = await handler.Handle(
            new RequestRefundOtpCommand(payment.Id, "email"),
            CancellationToken.None);

        result.Channel.ShouldBe(OtpChannel.Email);
        result.MaskedDestination.ShouldBe("masked-email:nguyenvana@example.com");
        emailSender.SentEmails.ShouldBe(["nguyenvana@example.com"]);
        smsSender.SentPhoneNumbers.ShouldBeEmpty();

        var challenge = await context.Set<OtpChallenge>()
            .SingleAsync(x => x.UserId == user.Id && x.Purpose == OtpPurpose.Refund);
        challenge.Channel.ShouldBe(OtpChannel.Email);
        challenge.Email.ShouldBe("nguyenvana@example.com");
        challenge.PendingPhoneNumber.ShouldBe(payment.Id.ToString("N"));
    }

    [Test]
    public async Task RefundIsRejectedForRegularRouteBooking()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userId = Guid.NewGuid();
        var now = new DateTimeOffset(2026, 7, 7, 10, 0, 0, TimeSpan.Zero);
        var trip = TripOnRoute("Regular", now.AddDays(5));
        var (booking, payment) = PaidSeatBooking(userId, trip, "BK-REFUND-REG", now);
        context.AddRange(trip.Route, trip, booking, payment);
        await context.SaveChangesAsync();

        var (handler, otpChallenge) = await CreateRefundHandlerAsync(
            context,
            userId,
            payment,
            now);

        var exception = await Should.ThrowAsync<ValidationException>(() =>
            handler.Handle(
                CreateRefundCommand(payment, otpChallenge),
                CancellationToken.None));

        exception.Errors["refund"].Single().ShouldContain("không hỗ trợ hoàn tiền");
        payment.RefundAmount.ShouldBe(0);
        payment.RefundStatus.ShouldBeNull();
        booking.BookingStatus.ShouldBe(BookingStatus.Confirmed);
    }

    [Test]
    public async Task RefundIsRejectedForSightseeingRouteBooking()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userId = Guid.NewGuid();
        var now = new DateTimeOffset(2026, 7, 7, 10, 0, 0, TimeSpan.Zero);
        var trip = TripOnRoute("SightseeingLoop", now.AddDays(5));
        var (booking, payment) = PaidSeatBooking(userId, trip, "BK-REFUND-SS", now);
        context.AddRange(trip.Route, trip, booking, payment);
        await context.SaveChangesAsync();

        var (handler, otpChallenge) = await CreateRefundHandlerAsync(
            context,
            userId,
            payment,
            now);

        var exception = await Should.ThrowAsync<ValidationException>(() =>
            handler.Handle(
                CreateRefundCommand(payment, otpChallenge),
                CancellationToken.None));

        exception.Errors["refund"].Single().ShouldContain("không hỗ trợ hoàn tiền");
        payment.RefundAmount.ShouldBe(0);
        payment.RefundStatus.ShouldBeNull();
        booking.BookingStatus.ShouldBe(BookingStatus.Confirmed);
    }

    [Test]
    public async Task RequestRefundOtpIsRejectedForSightseeingRouteBooking()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userId = Guid.NewGuid();
        var now = new DateTimeOffset(2026, 7, 7, 10, 0, 0, TimeSpan.Zero);
        var trip = TripOnRoute("SightseeingLoop", now.AddDays(2));
        var (booking, payment) = PaidSeatBooking(userId, trip, "BK-REFUND-SS70", now);
        var user = SeedCustomer(context, userId, "nguyenvana@example.com");
        context.AddRange(trip.Route, trip, booking, payment);
        await context.SaveChangesAsync();
        var handler = CreateRequestRefundOtpHandler(
            context,
            userId,
            now,
            new TestOtpSender(),
            new TestSmsOtpSender());

        var exception = await Should.ThrowAsync<ValidationException>(() =>
            handler.Handle(new RequestRefundOtpCommand(payment.Id), CancellationToken.None));

        user.Id.ShouldBe(userId);
        exception.Errors["refund"].Single().ShouldContain("không hỗ trợ hoàn tiền");
    }

    [Test]
    public async Task RefundCharterBookingZeroRefundsUnder24Hours()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userId = Guid.NewGuid();
        var now = new DateTimeOffset(2026, 7, 7, 10, 0, 0, TimeSpan.Zero);
        var (booking, payment) = PaidCharterBooking(userId, "BK-REFUND-CHARTER0", now, now.AddHours(5));
        context.AddRange(booking, payment);
        await context.SaveChangesAsync();

        var (handler, otpChallenge) = await CreateRefundHandlerAsync(
            context,
            userId,
            payment,
            now);

        // Huỷ dưới 24 giờ trước giờ khởi hành: refund = 0đ, BE đóng sổ booking trực tiếp,
        // KHÔNG yêu cầu OTP và KHÔNG gọi PayOS.
        var result = await handler.Handle(
            CreateRefundCommand(payment, otpChallenge),
            CancellationToken.None);

        payment.RefundStatus.ShouldBe("Refunded");
        payment.RefundAmount.ShouldBe(0m);
        payment.RefundRequestedAmount.ShouldBe(0m);
        payment.RefundMethod.ShouldBe("Manual");
        booking.BookingStatus.ShouldBe(BookingStatus.Cancelled);
        booking.PaymentStatus.ShouldBe("Refunded");
        result.ShouldNotBeNull();
    }

    [Test]
    public async Task RefundIsRejectedWhenOtpChallengeBelongsToDifferentPayment()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userId = Guid.NewGuid();
        var now = new DateTimeOffset(2026, 7, 7, 10, 0, 0, TimeSpan.Zero);
        var (firstBooking, firstPayment) = PaidCharterBooking(userId, "BK-REFUND-OTP-ONE", now);
        var (secondBooking, secondPayment) = PaidCharterBooking(userId, "BK-REFUND-OTP-TWO", now);
        context.AddRange(
            firstBooking,
            firstPayment,
            secondBooking,
            secondPayment);
        await context.SaveChangesAsync();
        var (handler, otpChallenge) = await CreateRefundHandlerAsync(
            context,
            userId,
            secondPayment,
            now);

        var exception = await Should.ThrowAsync<ValidationException>(() =>
            handler.Handle(
                CreateRefundCommand(firstPayment, otpChallenge),
                CancellationToken.None));

        exception.Errors["challengeId"].Single().ShouldContain("không khớp payment cần hoàn");
        firstPayment.RefundAmount.ShouldBe(0);
        firstPayment.RefundStatus.ShouldBeNull();
        firstBooking.BookingStatus.ShouldBe(BookingStatus.Confirmed);
    }

    [Test]
    public async Task RefundCharterBookingStillFollowsExistingPolicy()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userId = Guid.NewGuid();
        var now = new DateTimeOffset(2026, 7, 7, 10, 0, 0, TimeSpan.Zero);
        var booking = new Booking
        {
            UserId = userId,
            BookingType = Booking.CharterBookingType,
            BookingCode = "BK-REFUND-CHARTER",
            ContactName = "Nguyen Van A",
            ContactPhone = "0900000000",
            BookingStatus = BookingStatus.Confirmed,
            PaymentStatus = "Paid",
            DepartureDate = DateOnly.FromDateTime(now.AddDays(5).UtcDateTime),
            StartTime = new TimeOnly(8, 0),
            SubtotalAmount = 10000,
            TotalAmount = 10000,
            DepositAmount = 10000,
            RemainingAmount = 0
        };
        var payment = new Payment
        {
            Booking = booking,
            PaymentCode = "2000010",
            Provider = "PayOS",
            Amount = 10000,
            Currency = "VND",
            PaymentMethod = "PayOS",
            PaymentPurpose = "Full",
            PaymentStatus = "Paid",
            PaidAt = now.AddDays(-1)
        };
        context.AddRange(booking, payment);
        await context.SaveChangesAsync();

        var (handler, otpChallenge) = await CreateRefundHandlerAsync(
            context,
            userId,
            payment,
            now);

        var result = await handler.Handle(
            CreateRefundCommand(payment, otpChallenge),
            CancellationToken.None);

        result.RefundAmount.ShouldBe(10000);
        booking.BookingStatus.ShouldBe(BookingStatus.Cancelled);
    }

    [Test]
    public async Task GetRefundOtpOptionsReturnsAvailableMaskedChannels()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userId = Guid.NewGuid();
        var now = new DateTimeOffset(2026, 7, 7, 10, 0, 0, TimeSpan.Zero);
        var user = SeedCustomer(
            context,
            userId,
            email: "nguyenvana@example.com",
            phoneNumber: "0901234567",
            phoneVerifiedAt: now.AddDays(-1));
        var (booking, payment) = PaidCharterBooking(user.Id, "BK-REFUND-OTP-OPTIONS", now);
        context.AddRange(booking, payment);
        await context.SaveChangesAsync();
        var handler = new GetRefundOtpOptionsQueryHandler(
            context,
            new TestUserContext(user.Id),
            new TestOtpCodeService(),
            new FixedTimeProvider(now));

        var result = await handler.Handle(new GetRefundOtpOptionsQuery(payment.Id), CancellationToken.None);

        result.PaymentId.ShouldBe(payment.Id);
        result.RefundAmount.ShouldBe(10000);
        result.DefaultChannel.ShouldBe(OtpChannel.Phone);
        result.Channels.Select(x => x.Channel).ShouldBe([OtpChannel.Email, OtpChannel.Phone]);
        result.Channels.Single(x => x.Channel == OtpChannel.Email).MaskedDestination
            .ShouldBe("masked-email:nguyenvana@example.com");
        result.Channels.Single(x => x.Channel == OtpChannel.Phone).MaskedDestination
            .ShouldBe("masked-phone:+84901234567");
        result.Channels.Single(x => x.Channel == OtpChannel.Phone).IsDefault.ShouldBeTrue();
    }

    private const string RefundOtpCode = "123456";

    private static RequestRefundOtpCommandHandler CreateRequestRefundOtpHandler(
        IApplicationDbContext context,
        Guid userId,
        DateTimeOffset now,
        TestOtpSender emailSender,
        TestSmsOtpSender smsSender) =>
        new(
            context,
            new TestUserContext(userId),
            new TestSecretHasher(),
            new TestOtpCodeService(),
            emailSender,
            smsSender,
            new TestOtpPolicy(),
            new FixedTimeProvider(now));

    private static async Task<(RefundPaymentCommandHandler Handler, OtpChallenge Challenge)> CreateRefundHandlerAsync(
        IApplicationDbContext context,
        Guid userId,
        Payment payment,
        DateTimeOffset now,
        ICharterBookingPaymentGateway? gateway = null)
    {
        var secretHasher = new TestSecretHasher();
        var challenge = new OtpChallenge
        {
            UserId = userId,
            Purpose = OtpPurpose.Refund,
            Channel = OtpChannel.Email,
            Email = "nguyenvana@example.com",
            PendingPhoneNumber = payment.Id.ToString("N"),
            CodeHash = secretHasher.Hash(RefundOtpCode),
            ExpiresAt = now.AddMinutes(5),
            ResendAvailableAt = now.AddSeconds(30),
            MaxAttempts = 3
        };

        context.Set<OtpChallenge>().Add(challenge);
        await context.SaveChangesAsync(CancellationToken.None);

        return (
            new RefundPaymentCommandHandler(
                context,
                new TestUserContext(userId),
                gateway ?? new TestPaymentGateway(),
                secretHasher,
                new FixedTimeProvider(now)),
            challenge);
    }

    private static RefundPaymentCommand CreateRefundCommand(
        Payment payment,
        OtpChallenge challenge,
        string reason = "Doi lich",
        string accountName = "NGUYEN VAN A") =>
        new(
            payment.Id,
            reason,
            "970422",
            "123456789",
            accountName,
            challenge.Id,
            RefundOtpCode);

    private static User SeedCustomer(
        Infrastructure.Data.ApplicationDbContext context,
        Guid userId,
        string email,
        string? phoneNumber = null,
        DateTimeOffset? phoneVerifiedAt = null)
    {
        var role = new Role
        {
            Code = Roles.CustomerCode,
            SystemName = Roles.CustomerSystemName,
            DisplayName = "Customer"
        };
        var user = new User
        {
            Id = userId,
            FullName = "Nguyen Van A",
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            PhoneNumber = phoneNumber,
            NormalizedPhoneNumber = phoneNumber,
            PhoneVerifiedAt = phoneVerifiedAt,
            Role = role,
            Status = UserStatus.Active
        };
        context.AddRange(role, user);
        return user;
    }

    private static Trip TripOnRoute(string routeType, DateTimeOffset departureTime)
    {
        var route = new Route
        {
            RouteCode = $"R-{Guid.NewGuid():N}"[..20],
            RouteName = $"Route {routeType}",
            RouteType = routeType
        };
        return new Trip
        {
            Route = route,
            RouteId = route.Id,
            TripCode = $"TR-{Guid.NewGuid():N}"[..20],
            OperatingDate = DateOnly.FromDateTime(departureTime.UtcDateTime),
            DepartureTime = departureTime,
            ArrivalTime = departureTime.AddHours(1),
            CapacitySnapshot = 4
        };
    }

    private static (Booking Booking, Payment Payment) PaidSeatBooking(
        Guid userId,
        Trip trip,
        string bookingCode,
        DateTimeOffset now)
    {
        var booking = new Booking
        {
            UserId = userId,
            Trip = trip,
            TripId = trip.Id,
            BookingCode = bookingCode,
            ContactName = "Nguyen Van A",
            ContactPhone = "0900000000",
            BookingStatus = BookingStatus.Confirmed,
            PaymentStatus = "Paid",
            SubtotalAmount = 10000,
            TotalAmount = 10000,
            DepositAmount = 10000,
            RemainingAmount = 0
        };
        var payment = new Payment
        {
            Booking = booking,
            PaymentCode = $"20001{Math.Abs(bookingCode.GetHashCode()) % 100:D2}",
            Provider = "PayOS",
            Amount = 10000,
            Currency = "VND",
            PaymentMethod = "PayOS",
            PaymentPurpose = "Full",
            PaymentStatus = "Paid",
            PaidAt = now.AddDays(-1)
        };
        return (booking, payment);
    }

    private static (Booking Booking, Payment Payment) PaidCharterBooking(
        Guid userId,
        string bookingCode,
        DateTimeOffset now,
        DateTimeOffset? departureTime = null)
    {
        var resolvedDeparture = departureTime ?? now.AddDays(5);
        var booking = new Booking
        {
            UserId = userId,
            BookingType = Booking.CharterBookingType,
            BookingCode = bookingCode,
            ContactName = "Nguyen Van A",
            ContactPhone = "0900000000",
            BookingStatus = BookingStatus.Confirmed,
            PaymentStatus = "Paid",
            DepartureDate = DateOnly.FromDateTime(resolvedDeparture.UtcDateTime),
            StartTime = TimeOnly.FromDateTime(resolvedDeparture.UtcDateTime),
            SubtotalAmount = 10000,
            TotalAmount = 10000,
            DepositAmount = 10000,
            RemainingAmount = 0
        };
        var payment = new Payment
        {
            Booking = booking,
            PaymentCode = $"30001{Math.Abs(bookingCode.GetHashCode()) % 100:D2}",
            Provider = "PayOS",
            Amount = 10000,
            Currency = "VND",
            PaymentMethod = "PayOS",
            PaymentPurpose = "Full",
            PaymentStatus = "Paid",
            PaidAt = now.AddDays(-1)
        };
        return (booking, payment);
    }

    private sealed class TestSecretHasher : ISecretHasher
    {
        public string Hash(string secret) => $"hash:{secret}";

        public bool Verify(string secret, string hash) =>
            string.Equals(hash, Hash(secret), StringComparison.Ordinal);
    }

    private sealed class TestOtpCodeService : IOtpCodeService
    {
        public string GenerateCode() => RefundOtpCode;

        public string MaskEmail(string email) => $"masked-email:{email}";

        public string MaskPhone(string phoneNumber) => $"masked-phone:{phoneNumber}";
    }

    private sealed class TestOtpSender : IOtpSender
    {
        public List<string> SentEmails { get; } = [];

        public Task SendAsync(
            string email,
            string code,
            OtpPurpose purpose,
            string? recipientName,
            CancellationToken cancellationToken)
        {
            code.ShouldBe(RefundOtpCode);
            purpose.ShouldBe(OtpPurpose.Refund);
            SentEmails.Add(email);
            return Task.CompletedTask;
        }
    }

    private sealed class TestSmsOtpSender : ISmsOtpSender
    {
        public List<string> SentPhoneNumbers { get; } = [];

        public Task SendAsync(
            string phoneNumber,
            string code,
            OtpPurpose purpose,
            string? recipientName,
            CancellationToken cancellationToken)
        {
            code.ShouldBe(RefundOtpCode);
            purpose.ShouldBe(OtpPurpose.Refund);
            SentPhoneNumbers.Add(phoneNumber);
            return Task.CompletedTask;
        }
    }

    private sealed class TestOtpPolicy : IOtpPolicy
    {
        public int ExpirationMinutes => 5;

        public int ResendSeconds => 30;

        public int MaxAttempts => 3;
    }

    [Test]
    public async Task ManualRefundRecordsAdminRefundHistory()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var now = new DateTimeOffset(2026, 7, 7, 10, 0, 0, TimeSpan.Zero);
        var admin = SeedAdmin(context);
        var booking = new Booking
        {
            UserId = Guid.NewGuid(),
            BookingCode = "BK-MANUAL-REFUND",
            ContactName = "Nguyen Van A",
            ContactPhone = "0900000000",
            BookingStatus = BookingStatus.Confirmed,
            PaymentStatus = "Paid",
            SubtotalAmount = 10000,
            TotalAmount = 10000,
            DepositAmount = 10000,
            RemainingAmount = 0
        };
        var payment = new Payment
        {
            Booking = booking,
            PaymentCode = "2000001",
            Provider = "PayOS",
            Amount = 10000,
            Currency = "VND",
            PaymentMethod = "PayOS",
            PaymentPurpose = "Full",
            PaymentStatus = "Paid",
            RefundRequestedAmount = 7000,
            RefundMethod = "PayOS",
            RefundReferenceId = "RF-FAILED-001",
            RefundStatus = "Failed",
            RefundFailureReason = "PayOS payout failed",
            PaidAt = now.AddDays(-1)
        };
        context.AddRange(booking, payment);
        await context.SaveChangesAsync();

        var handler = new ManualRefundPaymentCommandHandler(
            context,
            new TestUserContext(admin.Id),
            new FixedTimeProvider(now));

        var result = await handler.Handle(
            new ManualRefundPaymentCommand(
                payment.Id,
                "Admin bank transfer",
                "BANK-TX-001",
                null,
                now.AddMinutes(-10)),
            CancellationToken.None);

        result.RefundAmount.ShouldBe(7000);
        result.RefundRequestedAmount.ShouldBe(7000);
        result.RefundMethod.ShouldBe("Manual");
        result.RefundReason.ShouldBe("Admin bank transfer");
        result.RefundReferenceId.ShouldBe("BANK-TX-001");
        result.RefundStatus.ShouldBe("Refunded");
        result.RefundFailureReason.ShouldBeNull();
        result.RefundProcessedByUserId.ShouldBe(admin.Id);
        result.RefundedAt.ShouldBe(now.AddMinutes(-10));
        booking.PaymentStatus.ShouldBe("Paid"); // partial refund không còn track ở booking-level
        booking.BookingStatus.ShouldBe(BookingStatus.Cancelled); // partial refund → Cancelled theo yêu cầu "đã hủy"
    }

    [Test]
    public async Task ManualRefundFullAmountMarksBookingRefunded()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var now = new DateTimeOffset(2026, 7, 7, 10, 0, 0, TimeSpan.Zero);
        var admin = SeedAdmin(context);
        var booking = new Booking
        {
            UserId = Guid.NewGuid(),
            BookingCode = "BK-MANUAL-FULL",
            ContactName = "Nguyen Van A",
            ContactPhone = "0900000000",
            BookingStatus = BookingStatus.Confirmed,
            PaymentStatus = "Paid",
            SubtotalAmount = 10000,
            TotalAmount = 10000,
            DepositAmount = 10000,
            RemainingAmount = 0
        };
        var payment = new Payment
        {
            Booking = booking,
            PaymentCode = "2000002",
            Provider = "PayOS",
            Amount = 10000,
            Currency = "VND",
            PaymentMethod = "PayOS",
            PaymentPurpose = "Full",
            PaymentStatus = "Paid",
            RefundRequestedAmount = 10000,
            RefundMethod = "PayOS",
            RefundReferenceId = "RF-FAILED-002",
            RefundStatus = "Failed",
            RefundFailureReason = "PayOS payout failed",
            PaidAt = now.AddDays(-1)
        };
        context.AddRange(booking, payment);
        await context.SaveChangesAsync();

        var handler = new ManualRefundPaymentCommandHandler(
            context,
            new TestUserContext(admin.Id),
            new FixedTimeProvider(now));

        var result = await handler.Handle(
            new ManualRefundPaymentCommand(payment.Id, "Admin bank transfer"),
            CancellationToken.None);

        result.RefundAmount.ShouldBe(10000);
        result.RefundRequestedAmount.ShouldBe(10000);
        result.RefundReferenceId.ShouldStartWith("MRF");
        booking.PaymentStatus.ShouldBe("Refunded");
        booking.BookingStatus.ShouldBe(BookingStatus.Cancelled);
        payment.PaymentStatus.ShouldBe("Refunded");
    }

    [Test]
    public async Task ManualRefundRequiresFailedPayOsRefundEvidence()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var now = new DateTimeOffset(2026, 7, 7, 10, 0, 0, TimeSpan.Zero);
        var admin = SeedAdmin(context);
        var booking = new Booking
        {
            UserId = Guid.NewGuid(),
            BookingCode = "BK-MANUAL-NO-EVIDENCE",
            ContactName = "Nguyen Van A",
            ContactPhone = "0900000000",
            BookingStatus = BookingStatus.Confirmed,
            PaymentStatus = "Paid",
            SubtotalAmount = 10000,
            TotalAmount = 10000,
            DepositAmount = 10000,
            RemainingAmount = 0
        };
        var payment = new Payment
        {
            Booking = booking,
            PaymentCode = "2000003",
            Provider = "PayOS",
            Amount = 10000,
            Currency = "VND",
            PaymentMethod = "PayOS",
            PaymentPurpose = "Full",
            PaymentStatus = "Paid",
            PaidAt = now.AddDays(-1)
        };
        context.AddRange(booking, payment);
        await context.SaveChangesAsync();

        var handler = new ManualRefundPaymentCommandHandler(
            context,
            new TestUserContext(admin.Id),
            new FixedTimeProvider(now));

        var exception = await Should.ThrowAsync<ValidationException>(() =>
            handler.Handle(
                new ManualRefundPaymentCommand(payment.Id, "Admin bank transfer"),
                CancellationToken.None));

        exception.Errors["refund"]
            .ShouldContain("Chỉ được ghi nhận hoàn tiền thủ công sau khi hệ thống đã thử hoàn qua PayOS và lưu trạng thái lỗi.");
        payment.RefundAmount.ShouldBe(0);
        payment.RefundStatus.ShouldBeNull();
    }

    private static CharterBookingDepositPaymentWebhook CreatePaidWebhook(long orderCode, long amount) =>
        new(
            "00",
            "success",
            true,
            new CharterBookingDepositPaymentWebhookData(
                orderCode,
                amount,
                null,
                null,
                null,
                null,
                "VND",
                "payment-link-id",
                "00",
                "success",
                null,
                null,
                null,
                null,
                null,
                null),
            "signature");

    private static int CountActivePromotionUsage(Infrastructure.Data.ApplicationDbContext context, Guid promotionId) =>
        context.Set<Booking>().Count(b => b.PromotionId == promotionId
            && b.BookingStatus != BookingStatus.Cancelled
            && b.BookingStatus != BookingStatus.Expired);

    private static User SeedAdmin(Infrastructure.Data.ApplicationDbContext context)
    {
        var role = new Role
        {
            Code = Roles.AdminCode,
            DisplayName = "Admin"
        };
        var user = new User
        {
            FullName = "Admin",
            PhoneNumber = "0900000001",
            Role = role,
            Status = UserStatus.Active
        };
        context.AddRange(role, user);
        return user;
    }

    private sealed class TestPaymentGateway(
        PaymentGatewayException? createPaymentException = null,
        PaymentGatewayException? getPaymentException = null,
        PaymentGatewayException? refundException = null)
        : ICharterBookingPaymentGateway
    {
        public List<CharterBookingDepositPaymentRequest> CreateRequests { get; } = [];

        public List<CharterBookingRefundPayoutRequest> RefundRequests { get; } = [];

        public Task<CharterBookingDepositPaymentResult> CreateDepositPaymentAsync(
            CharterBookingDepositPaymentRequest request,
            CancellationToken cancellationToken)
        {
            if (createPaymentException is not null)
            {
                throw createPaymentException;
            }

            CreateRequests.Add(request);
            return Task.FromResult(new CharterBookingDepositPaymentResult(
                "payment-link-id",
                "https://example.test/checkout",
                "qr",
                "PENDING"));
        }

        public Task<CharterBookingPaymentStatusResult> GetPaymentAsync(
            long orderCode,
            CancellationToken cancellationToken)
        {
            if (getPaymentException is not null)
            {
                throw getPaymentException;
            }

            return Task.FromResult(new CharterBookingPaymentStatusResult(
                orderCode,
                null,
                "PENDING",
                "payment-link-id",
                "https://example.test/checkout"));
        }

        public Task<CharterBookingPaymentCancellationResult> CancelPaymentAsync(
            long orderCode,
            string reason,
            CancellationToken cancellationToken) =>
            Task.FromResult(new CharterBookingPaymentCancellationResult(
                "payment-link-id",
                "CANCELLED",
                reason));

        public Task<CharterBookingRefundPayoutResult> CreateRefundPayoutAsync(
            CharterBookingRefundPayoutRequest request,
            CancellationToken cancellationToken)
        {
            if (refundException is not null)
            {
                throw refundException;
            }

            RefundRequests.Add(request);
            return Task.FromResult(new CharterBookingRefundPayoutResult(
                "payout-id",
                request.ReferenceId,
                "PENDING",
                null));
        }

        public Task<CharterBookingRefundPayoutResult?> GetRefundPayoutByReferenceIdAsync(
            string referenceId,
            CancellationToken cancellationToken) =>
            Task.FromResult<CharterBookingRefundPayoutResult?>(null);

        public bool IsValidWebhook(CharterBookingDepositPaymentWebhook webhook) => true;
    }

    private sealed class TestPaymentNotificationSender : IPaymentNotificationSender
    {
        public List<PaymentSucceededNotification> Notifications { get; } = [];
        public List<BoardingPassNotification> BoardingPasses { get; } = [];
        public List<ETicketNotification> ETickets { get; } = [];

        public Task SendPaymentSucceededAsync(
            PaymentSucceededNotification notification,
            CancellationToken cancellationToken)
        {
            Notifications.Add(notification);
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
