using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using SaigonWaterbus.Application.CharterBookings;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Application.UnitTests.TestInfrastructure;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using Shouldly;

namespace SaigonWaterbus.Application.UnitTests.CharterBookings;

/// <summary>
/// Verify charter booking webhook PayOS handler gửi email đúng khi thanh toán thành công.
/// </summary>
public class CharterBookingPaymentWebhookTests
{
    [Test]
    public async Task WebhookPaidWithPassengers_SendsETicketEmail()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userId = Guid.NewGuid();
        var booking = FullyPaidCharterBooking(userId, 30_000_000m);
        booking.ContactEmail = "customer@example.test";
        booking.Passengers.Add(ApprovedPassenger(booking.Id, "Nguyen Van A"));
        booking.Passengers.Add(ApprovedPassenger(booking.Id, "Tran Thi B"));
        booking.Passengers.Add(ApprovedPassenger(booking.Id, "Le Van C"));
        context.Add(booking);
        await context.SaveChangesAsync();

        var sender = new RecordingPaymentNotificationSender();
        var payment = booking.Payments.First();
        var now = new DateTimeOffset(2026, 8, 26, 0, 0, 0, TimeSpan.Zero);
        var timeProvider = new FixedTimeProvider(now);

        // Simulate: booking chưa paid → webhook paid → gửi notification
        payment.PaymentStatus = "Pending";
        payment.PaidAt = now;

        // Gọi trực tiếp SendETicketsIfFullyPaidAsync (giống webhook handler cho charter)
        await CharterBookingETicketSupport.SendETicketsIfFullyPaidAsync(
            context,
            timeProvider,
            sender,
            booking,
            payment,
            CancellationToken.None);

        // Verify: e-ticket cho tất cả passengers đã duyệt
        sender.ETickets.Count.ShouldBe(1,
            "Phải gửi email e-ticket charter.");
        sender.ETickets[0].Tickets.Count.ShouldBe(3,
            "Phải có 3 vé cho 3 hành khách đã duyệt.");

        // Verify: tickets được tạo trong DB
        var savedTickets = await context.Set<Ticket>().CountAsync();
        savedTickets.ShouldBe(3, "Phải tạo 3 ticket rows cho 3 passengers.");
    }

    [Test]
    public async Task WebhookPaidWithoutPassengers_SkipsETicketEmail()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userId = Guid.NewGuid();
        var booking = FullyPaidCharterBooking(userId, 30_000_000m);
        booking.ContactEmail = "customer@example.test";
        // Không có passengers
        context.Add(booking);
        await context.SaveChangesAsync();

        var sender = new RecordingPaymentNotificationSender();
        var payment = booking.Payments.First();
        var now = new DateTimeOffset(2026, 8, 26, 0, 0, 0, TimeSpan.Zero);
        var timeProvider = new FixedTimeProvider(now);

        payment.PaymentStatus = "Pending";
        payment.PaidAt = now;

        await CharterBookingETicketSupport.SendETicketsIfFullyPaidAsync(
            context,
            timeProvider,
            sender,
            booking,
            payment,
            CancellationToken.None);

        sender.ETickets.ShouldBeEmpty(
            "Chưa có hành khách → không gửi e-ticket.");
    }

    [Test]
    public async Task WebhookPaidWithPendingPassengers_SkipsETicketEmail()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userId = Guid.NewGuid();
        var booking = FullyPaidCharterBooking(userId, 30_000_000m);
        booking.ContactEmail = "customer@example.test";
        // Passenger đang pending (chưa duyệt)
        booking.Passengers.Add(new BookingPassenger
        {
            BookingId = booking.Id,
            FullName = "Pending Passenger",
            BirthYear = 1995,
            PassengerType = CharterBookingPassengerType.Adult.ToString(),
            ApprovalStatus = "Pending"
        });
        context.Add(booking);
        await context.SaveChangesAsync();

        var sender = new RecordingPaymentNotificationSender();
        var payment = booking.Payments.First();
        var now = new DateTimeOffset(2026, 8, 26, 0, 0, 0, TimeSpan.Zero);
        var timeProvider = new FixedTimeProvider(now);

        payment.PaymentStatus = "Pending";
        payment.PaidAt = now;

        await CharterBookingETicketSupport.SendETicketsIfFullyPaidAsync(
            context,
            timeProvider,
            sender,
            booking,
            payment,
            CancellationToken.None);

        sender.ETickets.ShouldBeEmpty(
            "Passenger pending → không gửi e-ticket (chờ admin duyệt trước).");
    }

    private static Booking FullyPaidCharterBooking(Guid userId, decimal totalAmount)
    {
        var booking = new Booking
        {
            BookingType = Booking.CharterBookingType,
            BookingCode = $"CB-{Guid.NewGuid():N}"[..14].ToUpperInvariant(),
            BookingStatus = BookingStatus.Confirmed,
            PaymentStatus = "Paid",
            UserId = userId,
            ContactName = "Nguyen Van A",
            ContactPhone = "0900000000",
            ContactEmail = "customer@example.test",
            DepartureDate = new DateOnly(2026, 9, 1),
            StartTime = new TimeOnly(8, 0),
            RentalUnit = BoatRentalUnit.Day,
            DurationValue = 1,
            AdultCount = 3,
            PassengerCount = 3,
            SubtotalAmount = totalAmount,
            TotalAmount = totalAmount,
            DepositAmount = totalAmount,
            RemainingAmount = 0m,
            Currency = "VND"
        };
        booking.Payments.Add(new Payment
        {
            PaymentCode = $"PAY{Guid.NewGuid():N}"[..20],
            Amount = totalAmount,
            Currency = "VND",
            PaymentMethod = "PayOS",
            PaymentPurpose = "Full",
            PaymentStatus = "Pending",
            PaidAt = DateTimeOffset.UtcNow
        });
        return booking;
    }

    private static BookingPassenger ApprovedPassenger(Guid bookingId, string name)
    {
        return new BookingPassenger
        {
            BookingId = bookingId,
            FullName = name,
            BirthYear = 1990,
            PassengerType = CharterBookingPassengerType.Adult.ToString(),
            ApprovalStatus = CharterBookingPassengerSupport.ApprovalStatusApproved,
            ReviewedAt = DateTimeOffset.UtcNow
        };
    }

    private sealed class RecordingPaymentNotificationSender : IPaymentNotificationSender
    {
        public List<PaymentSucceededNotification> PaymentSucceeded { get; } = [];
        public List<BoardingPassNotification> BoardingPasses { get; } = [];
        public List<ETicketNotification> ETickets { get; } = [];

        public Task SendPaymentSucceededAsync(PaymentSucceededNotification notification, CancellationToken cancellationToken)
        {
            PaymentSucceeded.Add(notification);
            return Task.CompletedTask;
        }

        public Task SendBoardingPassAsync(BoardingPassNotification notification, CancellationToken cancellationToken)
        {
            BoardingPasses.Add(notification);
            return Task.CompletedTask;
        }

        public Task SendETicketsAsync(ETicketNotification notification, CancellationToken cancellationToken)
        {
            ETickets.Add(notification);
            return Task.CompletedTask;
        }

        public Task SendCharterETicketsAsync(ETicketNotification notification, CancellationToken cancellationToken)
        {
            ETickets.Add(notification);
            return Task.CompletedTask;
        }

        public Task SendRefundReleasedAsync(RefundReleasedNotification notification, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
