using NUnit.Framework;
using SaigonWaterbus.Application.CharterBookings;
using SaigonWaterbus.Application.UnitTests.TestInfrastructure;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using Shouldly;

namespace SaigonWaterbus.Application.UnitTests.CharterBookings;

public class GetCharterBookingRefundPreviewQueryTests
{
    private static readonly DateTimeOffset Now = new(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task ReturnsCorrectOutstanding_WhenPartialRefundAlreadyApplied()
    {
        // Regression test for S6MZ7: trước đây bug filter theo PaymentStatus == "Refunded" → totalRefunded = 0
        // dù đã refund 1 phần 73.500đ. Phải tính từ payment.RefundAmount trên TẤT CẢ payments (không lọc status).
        await using var context = SeatFlowTestData.CreateContext();
        var userId = Guid.NewGuid();
        var booking = new Booking
        {
            BookingCode = "CB-20260816-S6MZ7",
            UserId = userId,
            ContactName = "Test",
            ContactPhone = "0900000000",
            BookingStatus = BookingStatus.Confirmed,
            PaymentStatus = "Paid",
            BookingType = Booking.CharterBookingType,
            DepartureDate = new DateOnly(2030, 1, 2), // 1 ngày trước → policy 0.7
            StartTime = new TimeOnly(8, 0),
            TotalAmount = 105_000m
        };
        var payment = new Payment
        {
            Booking = booking,
            PaymentCode = "PAY-PARTIAL",
            Provider = "PayOS",
            PaymentMethod = "PayOS",
            Amount = 105_000m,
            Currency = "VND",
            PaymentPurpose = "Full",
            // Partial refund: payment vẫn ở "Paid", nhưng RefundAmount = 73.500.
            PaymentStatus = "Paid",
            RefundAmount = 73_500m,
            RefundStatus = "Refunded",
            RefundedAt = Now.AddMinutes(-10)
        };
        context.AddRange(booking, payment);
        await context.SaveChangesAsync();

        var handler = new GetCharterBookingRefundPreviewQueryHandler(
            context,
            new TestUserContext(userId),
            new FixedTimeProvider(Now));

        var result = await handler.Handle(
            new GetCharterBookingRefundPreviewQuery(booking.Id),
            CancellationToken.None);

        result.TotalPaidAmount.ShouldBe(105_000m);
        result.TotalRefundedAmount.ShouldBe(73_500m);
        result.OutstandingRefundAmount.ShouldBe(31_500m);
        result.PolicyPercent.ShouldBe(0.7m);
        result.CanRequestRefund.ShouldBeTrue();
        result.IsPartiallyRefunded.ShouldBeTrue();
        result.IsFullyRefunded.ShouldBeFalse();
        result.RefundablePayments.Count.ShouldBe(1);
        result.RefundablePayments[0].PaidAmount.ShouldBe(105_000m);
        result.RefundablePayments[0].AlreadyRefundedAmount.ShouldBe(73_500m);
        result.RefundablePayments[0].AvailableRefundAmount.ShouldBe(31_500m);
    }

    [Test]
    public async Task ReturnsZeroOutstanding_WhenFullyRefunded()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userId = Guid.NewGuid();
        var booking = new Booking
        {
            BookingCode = "CB-FULL",
            UserId = userId,
            ContactName = "Test",
            ContactPhone = "0900000000",
            BookingStatus = BookingStatus.Cancelled,
            PaymentStatus = "Refunded",
            BookingType = Booking.CharterBookingType,
            DepartureDate = new DateOnly(2030, 1, 5),
            StartTime = new TimeOnly(8, 0),
            TotalAmount = 105_000m
        };
        var payment = new Payment
        {
            Booking = booking,
            PaymentCode = "PAY-FULL",
            Provider = "PayOS",
            PaymentMethod = "PayOS",
            Amount = 105_000m,
            Currency = "VND",
            PaymentPurpose = "Full",
            PaymentStatus = "Refunded",
            RefundAmount = 105_000m,
            RefundStatus = "Refunded",
            RefundedAt = Now.AddMinutes(-10)
        };
        context.AddRange(booking, payment);
        await context.SaveChangesAsync();

        var handler = new GetCharterBookingRefundPreviewQueryHandler(
            context,
            new TestUserContext(userId),
            new FixedTimeProvider(Now));

        var result = await handler.Handle(
            new GetCharterBookingRefundPreviewQuery(booking.Id),
            CancellationToken.None);

        result.OutstandingRefundAmount.ShouldBe(0m);
        result.CanRequestRefund.ShouldBeFalse();
        // Payment đã Refunded (status Refunded) nên totalPaid = 0 (chỉ tính Paid), isFullyRefunded = false
        // vì outstanding đã được track từ RefundAmount tổng = 105.000 khi totalPaid = 0.
        // FE check refundSummary.totalRefundedAmount > 0 để biết booking đã hoàn đủ.
        result.TotalRefundedAmount.ShouldBe(105_000m);
    }

    [Test]
    public async Task CanRequestRefundTrue_EvenWhenPolicyIsZero_ForZeroRefundClosing()
    {
        // Regression test for XDESL/2Z6AM: hủy < 24h trước giờ khởi hành.
        // Booking chưa refund gì cả, policyPercent = 0 → vẫn cho phép "đóng sổ" 0đ.
        await using var context = SeatFlowTestData.CreateContext();
        var userId = Guid.NewGuid();
        var booking = new Booking
        {
            BookingCode = "CB-UNDER24H",
            UserId = userId,
            ContactName = "Test",
            ContactPhone = "0900000000",
            BookingStatus = BookingStatus.Cancelled,
            PaymentStatus = "Paid",
            BookingType = Booking.CharterBookingType,
            DepartureDate = new DateOnly(2030, 1, 1), // < 24h
            StartTime = new TimeOnly(2, 0),
            TotalAmount = 105_000m
        };
        var payment = new Payment
        {
            Booking = booking,
            PaymentCode = "PAY-UNDER24H",
            Provider = "PayOS",
            PaymentMethod = "PayOS",
            Amount = 105_000m,
            Currency = "VND",
            PaymentPurpose = "Full",
            PaymentStatus = "Paid"
        };
        context.AddRange(booking, payment);
        await context.SaveChangesAsync();

        var handler = new GetCharterBookingRefundPreviewQueryHandler(
            context,
            new TestUserContext(userId),
            new FixedTimeProvider(Now));

        var result = await handler.Handle(
            new GetCharterBookingRefundPreviewQuery(booking.Id),
            CancellationToken.None);

        result.TotalPaidAmount.ShouldBe(105_000m);
        result.TotalRefundedAmount.ShouldBe(0m);
        result.OutstandingRefundAmount.ShouldBe(105_000m);
        result.PolicyPercent.ShouldBe(0m);
        result.CanRequestRefund.ShouldBeTrue(); // Phải = true để cho đóng sổ 0đ
        result.IsPartiallyRefunded.ShouldBeFalse();
        result.IsFullyRefunded.ShouldBeFalse();
        // available = 0 vì policy = 0 không cho hoàn → không thêm vào RefundablePayments.
        result.RefundablePayments.Count.ShouldBe(0);
    }

    [Test]
    public async Task CannotRequestRefund_WhenBookingCompleted()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userId = Guid.NewGuid();
        var booking = new Booking
        {
            BookingCode = "CB-COMPLETED",
            UserId = userId,
            ContactName = "Test",
            ContactPhone = "0900000000",
            BookingStatus = BookingStatus.Completed,
            PaymentStatus = "Paid",
            BookingType = Booking.CharterBookingType,
            DepartureDate = new DateOnly(2030, 1, 5),
            StartTime = new TimeOnly(8, 0),
            TotalAmount = 105_000m
        };
        var payment = new Payment
        {
            Booking = booking,
            PaymentCode = "PAY-C",
            Provider = "PayOS",
            PaymentMethod = "PayOS",
            Amount = 105_000m,
            Currency = "VND",
            PaymentPurpose = "Full",
            PaymentStatus = "Paid"
        };
        context.AddRange(booking, payment);
        await context.SaveChangesAsync();

        var handler = new GetCharterBookingRefundPreviewQueryHandler(
            context,
            new TestUserContext(userId),
            new FixedTimeProvider(Now));

        var result = await handler.Handle(
            new GetCharterBookingRefundPreviewQuery(booking.Id),
            CancellationToken.None);

        result.CanRequestRefund.ShouldBeFalse();
    }
}
