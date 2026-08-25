using NUnit.Framework;
using Microsoft.EntityFrameworkCore;
using SaigonWaterbus.Application.CharterBookings;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Application.UnitTests.TestInfrastructure;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using Shouldly;

namespace SaigonWaterbus.Application.UnitTests.CharterBookings;

/// <summary>
/// Verify flow gửi email mã vé charter sau khi thanh toán đủ 100%.
///
///   - Trả 100% ngay + đã có hành khách → gửi email mã vé luôn.
///   - Trả cọc 50% → KHÔNG gửi mã vé (chỉ gửi xác nhận thanh toán).
///   - Trả đủ 100% + chưa có hành khách → chưa gửi mã vé (chờ import sau).
/// </summary>
public class CharterBookingPaymentETicketTests
{
    [Test]
    public async Task FullPaymentWithExistingPassengers_SendsCharterETickets()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userId = Guid.NewGuid();
        var booking = FullyPaidCharterBooking(userId, totalAmount: 30_000_000m);
        booking.ContactEmail = "customer@example.test";
        booking.Passengers.Add(ApprovedPassenger(booking.Id, "Nguyen Van A"));
        booking.Passengers.Add(ApprovedPassenger(booking.Id, "Tran Thi B"));
        context.Add(booking);
        await context.SaveChangesAsync();

        var sender = new RecordingCharterETicketSender();
        var payment = booking.Payments.First();

        await CharterBookingETicketSupport.SendETicketsIfFullyPaidAsync(
            context,
            new FixedTimeProvider(DateTimeOffset.UtcNow),
            sender,
            booking,
            payment,
            CancellationToken.None);

        sender.ETickets.Count.ShouldBe(1);
        sender.ETickets[0].Tickets.Count.ShouldBe(2,
            "Mỗi hành khách đã duyệt phải có 1 mục trong email.");
        sender.ETickets[0].BookingQrToken.ShouldNotBeNullOrWhiteSpace();

        var savedTickets = await context.Set<Ticket>().CountAsync();
        savedTickets.ShouldBe(2);
    }

    [Test]
    public async Task FullPaymentWithoutPassengers_DoesNotSendETickets()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userId = Guid.NewGuid();
        var booking = FullyPaidCharterBooking(userId, totalAmount: 30_000_000m);
        booking.ContactEmail = "customer@example.test";
        context.Add(booking);
        await context.SaveChangesAsync();

        var sender = new RecordingCharterETicketSender();
        var payment = booking.Payments.First();

        await CharterBookingETicketSupport.SendETicketsIfFullyPaidAsync(
            context,
            new FixedTimeProvider(DateTimeOffset.UtcNow),
            sender,
            booking,
            payment,
            CancellationToken.None);

        sender.ETickets.ShouldBeEmpty(
            "Chưa có hành khách thì chưa có gì để gửi. Email sẽ được gửi sau khi khách import danh sách.");
    }

    [Test]
    public async Task DepositPayment_DoesNotSendETickets()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userId = Guid.NewGuid();
        var booking = DepositPaidCharterBooking(userId, totalAmount: 30_000_000m, depositAmount: 15_000_000m);
        booking.ContactEmail = "customer@example.test";
        booking.Passengers.Add(ApprovedPassenger(booking.Id, "Nguyen Van A"));
        context.Add(booking);
        await context.SaveChangesAsync();

        var sender = new RecordingCharterETicketSender();
        var payment = booking.Payments.First();

        await CharterBookingETicketSupport.SendETicketsIfFullyPaidAsync(
            context,
            new FixedTimeProvider(DateTimeOffset.UtcNow),
            sender,
            booking,
            payment,
            CancellationToken.None);

        sender.ETickets.ShouldBeEmpty(
            "Cọc 50% chưa đủ → KHÔNG gửi mã vé, chờ khách trả nốt phần còn lại.");
        booking.RemainingAmount.ShouldBe(15_000_000m);
    }

    [Test]
    public async Task FullPaymentCallIsIdempotent_DoesNotResendTwice()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userId = Guid.NewGuid();
        var booking = FullyPaidCharterBooking(userId, totalAmount: 30_000_000m);
        booking.ContactEmail = "customer@example.test";
        booking.Passengers.Add(ApprovedPassenger(booking.Id, "Nguyen Van A"));
        context.Add(booking);
        await context.SaveChangesAsync();

        var sender = new RecordingCharterETicketSender();
        var payment = booking.Payments.First();
        var timeProvider = new FixedTimeProvider(DateTimeOffset.UtcNow);

        // Lần 1: lúc thanh toán đủ → gửi e-ticket.
        await CharterBookingETicketSupport.SendETicketsIfFullyPaidAsync(
            context, timeProvider, sender, booking, payment, CancellationToken.None);
        sender.ETickets.Count.ShouldBe(1);

        // Lần 2: gọi lại (idempotency) → vẫn gửi nhưng chỉ dựa trên existing tickets,
        // không tạo duplicate Ticket rows.
        await CharterBookingETicketSupport.SendETicketsIfFullyPaidAsync(
            context, timeProvider, sender, booking, payment, CancellationToken.None);

        var savedTickets = await context.Set<Ticket>().CountAsync();
        savedTickets.ShouldBe(1, "Không được tạo thêm Ticket row trùng.");
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
            DepartureDate = new DateOnly(2030, 1, 1),
            StartTime = new TimeOnly(8, 0),
            RentalUnit = BoatRentalUnit.Day,
            DurationValue = 1,
            AdultCount = 2,
            PassengerCount = 2,
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
            PaymentMethod = "BankTransfer",
            PaymentPurpose = "Full",
            PaymentStatus = "Paid",
            PaidAt = DateTimeOffset.UtcNow
        });
        return booking;
    }

    private static Booking DepositPaidCharterBooking(Guid userId, decimal totalAmount, decimal depositAmount)
    {
        var booking = new Booking
        {
            BookingType = Booking.CharterBookingType,
            BookingCode = $"CB-{Guid.NewGuid():N}"[..14].ToUpperInvariant(),
            BookingStatus = BookingStatus.Confirmed,
            PaymentStatus = "DepositPaid",
            UserId = userId,
            ContactName = "Nguyen Van A",
            ContactPhone = "0900000000",
            ContactEmail = "customer@example.test",
            DepartureDate = new DateOnly(2030, 1, 1),
            StartTime = new TimeOnly(8, 0),
            RentalUnit = BoatRentalUnit.Day,
            DurationValue = 1,
            AdultCount = 2,
            PassengerCount = 2,
            SubtotalAmount = totalAmount,
            TotalAmount = totalAmount,
            DepositAmount = depositAmount,
            RemainingAmount = totalAmount - depositAmount,
            Currency = "VND"
        };
        booking.Payments.Add(new Payment
        {
            PaymentCode = $"PAY{Guid.NewGuid():N}"[..20],
            Amount = depositAmount,
            Currency = "VND",
            PaymentMethod = "BankTransfer",
            PaymentPurpose = "Deposit",
            PaymentStatus = "Paid",
            PaidAt = DateTimeOffset.UtcNow
        });
        return booking;
    }

    private static BookingPassenger ApprovedPassenger(Guid bookingId, string name)
    {
        var currentYear = DateTime.UtcNow.Year;
        return new BookingPassenger
        {
            BookingId = bookingId,
            FullName = name,
            BirthYear = currentYear - 30,
            PassengerType = CharterBookingPassengerType.Adult.ToString(),
            ApprovalStatus = CharterBookingPassengerSupport.ApprovalStatusApproved,
            ReviewedAt = DateTimeOffset.UtcNow
        };
    }

    private sealed class RecordingCharterETicketSender : IPaymentNotificationSender
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
