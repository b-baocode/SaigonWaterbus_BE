using NUnit.Framework;
using SaigonWaterbus.Application.Common.Exceptions;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Application.Payments;
using SaigonWaterbus.Application.UnitTests.TestInfrastructure;
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
            RemainingAmount = 10000
        };
        context.Add(booking);
        await context.SaveChangesAsync();

        var handler = new CreatePaymentCommandHandler(
            context,
            new TestUserContext(userId),
            new TestPaymentGateway(),
            new TestPaymentNotificationSender(),
            TimeProvider.System);

        var result = await handler.Handle(
            new CreatePaymentCommand(booking.Id, BookingPaymentOption.Deposit),
            CancellationToken.None);

        result.Amount.ShouldBe(5000);
        result.PaymentStatus.ShouldBe("Pending");
        result.CheckoutUrl.ShouldBe("https://example.test/checkout");
        context.Set<Payment>().Count().ShouldBe(1);
        context.Set<Payment>().Single().PaymentPurpose.ShouldBe("Deposit");
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
            Status = "Active"
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
        promotion.UsageCount.ShouldBe(1);
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
            Status = "Active"
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
        promotion.UsageCount.ShouldBe(0);
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

        booking.PaymentStatus.ShouldBe("Unpaid");
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
        var ticketItem = new TicketItem
        {
            Booking = booking,
            BookingPassenger = passenger,
            UnitPrice = 10000
        };
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
        context.AddRange(booking, passenger, ticketItem, payment);
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
        ticket.TicketItemId.ShouldBe(ticketItem.Id);
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

    private sealed class TestPaymentGateway(
        PaymentGatewayException? createPaymentException = null,
        PaymentGatewayException? getPaymentException = null)
        : ICharterBookingPaymentGateway
    {
        public Task<CharterBookingDepositPaymentResult> CreateDepositPaymentAsync(
            CharterBookingDepositPaymentRequest request,
            CancellationToken cancellationToken)
        {
            if (createPaymentException is not null)
            {
                throw createPaymentException;
            }

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
            CancellationToken cancellationToken) =>
            Task.FromResult(new CharterBookingRefundPayoutResult(
                "payout-id",
                request.ReferenceId,
                "PENDING",
                null));

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
    }
}
