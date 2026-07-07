using NUnit.Framework;
using SaigonWaterbus.Application.Common.Exceptions;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Application.Payments;
using SaigonWaterbus.Application.UnitTests.TestInfrastructure;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using Shouldly;

namespace SaigonWaterbus.Application.UnitTests.Bookings;

public class BookingHoldAndETicketTests
{
    [Test]
    public async Task WebhookPaidRegularBookingSendsETicketEmailsWithBookingQr()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var booking = new Booking
        {
            UserId = Guid.NewGuid(),
            BookingCode = "BK-ETICKET",
            ContactName = "Nguyen Van A",
            ContactPhone = "0900000000",
            ContactEmail = "booker@gmail.com",
            BookingStatus = BookingStatus.PendingPayment,
            PaymentStatus = "Unpaid",
            SubtotalAmount = 20000,
            TotalAmount = 20000,
            RemainingAmount = 20000
        };
        var passengerWithEmail = new BookingPassenger
        {
            Booking = booking,
            FullName = "Nguyen Van B",
            PassengerType = "ADULT",
            Email = "passenger-b@gmail.com",
            UnitPrice = 10000
        };
        var passengerWithoutEmail = new BookingPassenger
        {
            Booking = booking,
            FullName = "Nguyen Van C",
            PassengerType = "CHILD",
            UnitPrice = 10000
        };
        var payment = new Payment
        {
            Booking = booking,
            PaymentCode = "2000001",
            Provider = "PayOS",
            Amount = 20000,
            Currency = "VND",
            PaymentMethod = "PayOS",
            PaymentPurpose = "Full",
            PaymentStatus = "Pending"
        };
        context.AddRange(booking, passengerWithEmail, passengerWithoutEmail, payment);
        await context.SaveChangesAsync();

        var sender = new RecordingPaymentNotificationSender();
        var handler = new HandlePaymentWebhookCommandHandler(
            context,
            new PaidPaymentGateway(),
            sender,
            TimeProvider.System);

        await handler.Handle(
            new HandlePaymentWebhookCommand(CreatePaidWebhook(2000001, 20000)),
            CancellationToken.None);

        // QR chung được sinh với prefix BK.
        booking.CharterBookingQrToken.ShouldNotBeNullOrWhiteSpace();
        booking.CharterBookingQrToken.ShouldStartWith("BK");

        // Người đặt nhận 1 email vé tổng chứa cả 2 vé + QR chung.
        var eTicket = sender.ETickets.ShouldHaveSingleItem();
        eTicket.Booking.Email.ShouldBe("booker@gmail.com");
        eTicket.BookingQrToken.ShouldBe(booking.CharterBookingQrToken);
        eTicket.Tickets.Count.ShouldBe(2);
        eTicket.Tickets.ShouldAllBe(x => !string.IsNullOrWhiteSpace(x.QrToken));

        // Hành khách có email nhận thêm boarding pass riêng; hành khách không email thì không.
        var boardingPass = sender.BoardingPasses.ShouldHaveSingleItem();
        boardingPass.Booking.Email.ShouldBe("passenger-b@gmail.com");
        var passengerTicket = context.Tickets.Single(x => x.BookingPassengerId == passengerWithEmail.Id);
        boardingPass.QrToken.ShouldBe(passengerTicket.QrToken);

        // Không gửi email xác nhận thanh toán kiểu cũ cho booking thường.
        sender.Notifications.ShouldBeEmpty();
    }

    [Test]
    public async Task CreatePaymentExpiresRegularBookingWhenHoldExpired()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userId = Guid.NewGuid();
        var booking = new Booking
        {
            UserId = userId,
            BookingCode = "BK-EXPIRED-HOLD",
            ContactName = "Nguyen Van A",
            ContactPhone = "0900000000",
            BookingStatus = BookingStatus.PendingPayment,
            PaymentStatus = "Unpaid",
            SubtotalAmount = 10000,
            TotalAmount = 10000,
            RemainingAmount = 10000,
            HoldExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1)
        };
        context.Add(booking);
        await context.SaveChangesAsync();

        var handler = new CreatePaymentCommandHandler(
            context,
            new TestUserContext(userId),
            new PaidPaymentGateway(),
            new RecordingPaymentNotificationSender(),
            TimeProvider.System);

        await Should.ThrowAsync<ValidationException>(() =>
            handler.Handle(new CreatePaymentCommand(booking.Id), CancellationToken.None));

        booking.BookingStatus.ShouldBe(BookingStatus.Expired);
        context.Set<Payment>().Count().ShouldBe(0);
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

    private sealed class RecordingPaymentNotificationSender : IPaymentNotificationSender
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
    }

    private sealed class PaidPaymentGateway : ICharterBookingPaymentGateway
    {
        public Task<CharterBookingDepositPaymentResult> CreateDepositPaymentAsync(
            CharterBookingDepositPaymentRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new CharterBookingDepositPaymentResult(
                "link-id",
                "https://example.test/checkout",
                "qr",
                "Pending"));

        public Task<CharterBookingPaymentStatusResult> GetPaymentAsync(
            long orderCode,
            CancellationToken cancellationToken) =>
            Task.FromResult(new CharterBookingPaymentStatusResult(
                orderCode,
                null,
                "Pending",
                "link-id",
                "https://example.test/checkout"));

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
            Task.FromResult(new CharterBookingRefundPayoutResult("payout-id", "ref", "Pending", null));

        public Task<CharterBookingRefundPayoutResult?> GetRefundPayoutByReferenceIdAsync(
            string referenceId,
            CancellationToken cancellationToken) =>
            Task.FromResult<CharterBookingRefundPayoutResult?>(null);

        public bool IsValidWebhook(CharterBookingDepositPaymentWebhook webhook) => true;
    }
}
