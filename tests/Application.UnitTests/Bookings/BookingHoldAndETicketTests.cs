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

        sender.ETickets.Count.ShouldBe(2);

        // Người đặt nhận 1 email vé tổng chứa cả 2 vé + QR chung.
        var eTicket = sender.ETickets.Single(x => x.Booking.Email == "booker@gmail.com");
        eTicket.Booking.Email.ShouldBe("booker@gmail.com");
        eTicket.BookingQrToken.ShouldBe(booking.CharterBookingQrToken);
        eTicket.Tickets.Count.ShouldBe(2);
        eTicket.Tickets.ShouldAllBe(x => !string.IsNullOrWhiteSpace(x.QrToken));

        // Hành khách có email nhận thêm e-ticket riêng; hành khách không email thì không.
        var passengerETicket = sender.ETickets.Single(x => x.Booking.Email == "passenger-b@gmail.com");
        passengerETicket.BookingQrToken.ShouldBeNull();
        passengerETicket.Tickets.ShouldHaveSingleItem();
        var passengerTicket = context.Tickets.Single(x => x.BookingPassengerId == passengerWithEmail.Id);
        passengerETicket.Tickets.Single().QrToken.ShouldBe(passengerTicket.QrToken);
        sender.BoardingPasses.ShouldBeEmpty();

        // Không gửi email xác nhận thanh toán kiểu cũ cho booking thường.
        sender.Notifications.ShouldBeEmpty();
    }

    [Test]
    public async Task WebhookPaidRoundTripBookingSendsETicketWithBothLegs()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var outboundTrip = CreateTrip("TR-OUT");
        var returnTrip = CreateTrip("TR-RET");
        var booking = new Booking
        {
            UserId = Guid.NewGuid(),
            Trip = outboundTrip,
            ReturnTrip = returnTrip,
            BookingCode = "BK-ROUNDTRIP-ET",
            ContactName = "Nguyen Van A",
            ContactPhone = "0900000000",
            ContactEmail = "booker@gmail.com",
            BookingStatus = BookingStatus.PendingPayment,
            PaymentStatus = "Unpaid",
            SubtotalAmount = 20000,
            TotalAmount = 20000,
            RemainingAmount = 20000
        };
        var outboundPassenger = new BookingPassenger
        {
            Booking = booking,
            Trip = outboundTrip,
            FullName = "Khach Chieu Di",
            PassengerType = "ADULT",
            UnitPrice = 10000
        };
        var returnPassenger = new BookingPassenger
        {
            Booking = booking,
            Trip = returnTrip,
            FullName = "Khach Chieu Ve",
            PassengerType = "ADULT",
            Email = "passenger-return@gmail.com",
            UnitPrice = 10000
        };
        var payment = new Payment
        {
            Booking = booking,
            PaymentCode = "2000002",
            Provider = "PayOS",
            Amount = 20000,
            Currency = "VND",
            PaymentMethod = "PayOS",
            PaymentPurpose = "Full",
            PaymentStatus = "Pending"
        };
        context.AddRange(outboundTrip, returnTrip, booking, outboundPassenger, returnPassenger, payment);
        await context.SaveChangesAsync();

        var sender = new RecordingPaymentNotificationSender();
        var handler = new HandlePaymentWebhookCommandHandler(
            context,
            new PaidPaymentGateway(),
            sender,
            TimeProvider.System);

        await handler.Handle(
            new HandlePaymentWebhookCommand(CreatePaidWebhook(2000002, 20000)),
            CancellationToken.None);

        // Email tổng cho người đặt: đủ 2 vé + 2 legs đúng chiều.
        var bookerETicket = sender.ETickets.Single(x => x.Booking.Email == "booker@gmail.com");
        bookerETicket.Tickets.Count.ShouldBe(2);
        bookerETicket.TripCode.ShouldBe("TR-OUT");
        bookerETicket.Legs.ShouldNotBeNull();
        bookerETicket.Legs.Count.ShouldBe(2);
        bookerETicket.Legs[0].TripCode.ShouldBe("TR-OUT");
        bookerETicket.Legs[0].Tickets.ShouldHaveSingleItem().PassengerName.ShouldBe("Khach Chieu Di");
        bookerETicket.Legs[1].TripCode.ShouldBe("TR-RET");
        bookerETicket.Legs[1].Tickets.ShouldHaveSingleItem().PassengerName.ShouldBe("Khach Chieu Ve");

        // Email riêng của hành khách chiều về hiển thị trip chiều về.
        var passengerETicket = sender.ETickets.Single(x => x.Booking.Email == "passenger-return@gmail.com");
        passengerETicket.TripCode.ShouldBe("TR-RET");
        passengerETicket.Legs.ShouldBeNull();
        passengerETicket.Tickets.ShouldHaveSingleItem().PassengerName.ShouldBe("Khach Chieu Ve");
    }

    private static Trip CreateTrip(string tripCode) =>
        new()
        {
            Route = new Route
            {
                RouteCode = $"R-{tripCode}",
                RouteName = $"Route {tripCode}"
            },
            TripCode = tripCode,
            OperatingDate = new DateOnly(2026, 7, 20),
            DepartureTime = new DateTimeOffset(2026, 7, 20, 8, 0, 0, TimeSpan.Zero),
            ArrivalTime = new DateTimeOffset(2026, 7, 20, 9, 0, 0, TimeSpan.Zero),
            CapacitySnapshot = 2
        };

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
