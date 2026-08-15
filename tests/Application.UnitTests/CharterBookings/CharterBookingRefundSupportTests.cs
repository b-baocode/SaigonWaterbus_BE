using NUnit.Framework;
using SaigonWaterbus.Application.CharterBookings;
using SaigonWaterbus.Application.UnitTests.TestInfrastructure;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using Shouldly;

namespace SaigonWaterbus.Application.UnitTests.CharterBookings;

public class CharterBookingRefundSupportTests
{
    private static readonly DateTimeOffset Now = new(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);
    // Default: departure 2030-01-04 08:00 VN = 2030-01-04 01:00 UTC → TimeUntilDeparture = 3 days (≥ 3d → 100% refund).

    [Test]
    public void BuildSummary_ReturnsZeroRefund_WhenNothingPaid()
    {
        var booking = CharterBooking(departure: new DateOnly(2030, 1, 4));

        var summary = CharterBookingRefundSupport.BuildSummary(booking, Now);

        summary.TotalPaidAmount.ShouldBe(0m);
        summary.TotalRefundedAmount.ShouldBe(0m);
        summary.OutstandingRefundAmount.ShouldBe(0m);
        summary.CanRequestRefund.ShouldBeFalse();
    }

    [Test]
    public void BuildSummary_ReturnsFullRefund_WhenDepartureIsAtLeast3DaysAway()
    {
        // departure 2030-01-04 08:00 VN → 3 days từ now (2030-01-01 00:00 UTC = 2030-01-01 07:00 VN)
        // time-until-departure = 3 days - 7h = 65 hours. Đủ ≥ 3 ngày → 100%.
        var booking = CharterBooking(
            departure: new DateOnly(2030, 1, 4),
            payments: [PaidPayment(amount: 1_500_000m)]);

        var summary = CharterBookingRefundSupport.BuildSummary(booking, Now);

        summary.PolicyPercent.ShouldBe(1.0m);
        summary.CanRequestRefund.ShouldBeTrue();
        summary.PolicyMessage.ShouldContain("100%");
        summary.OutstandingRefundAmount.ShouldBe(1_500_000m);
    }

    [Test]
    public void BuildSummary_Returns70Percent_WhenBetween24HoursAnd3Days()
    {
        // departure 2030-01-02 08:00 VN → còn khoảng 25 giờ từ now.
        var booking = CharterBooking(
            departure: new DateOnly(2030, 1, 2),
            payments: [PaidPayment(amount: 1_000_000m)]);

        var summary = CharterBookingRefundSupport.BuildSummary(booking, Now);

        summary.PolicyPercent.ShouldBe(0.7m);
        summary.CanRequestRefund.ShouldBeTrue();
        summary.PolicyMessage.ShouldContain("70%");
    }

    [Test]
    public void BuildSummary_ReturnsZero_WhenUnder24Hours()
    {
        // departure 2030-01-01 09:00 VN = 2030-01-01 02:00 UTC → còn 2h từ now.
        var booking = CharterBooking(
            departure: new DateOnly(2030, 1, 1),
            startTime: new TimeOnly(9, 0),
            payments: [PaidPayment(amount: 1_000_000m)]);

        var summary = CharterBookingRefundSupport.BuildSummary(booking, Now);

        summary.PolicyPercent.ShouldBe(0m);
        summary.CanRequestRefund.ShouldBeFalse();
        summary.PolicyMessage.ShouldContain("không được hoàn");
    }

    [Test]
    public void BuildSummary_CapsOutstandingByAlreadyRefunded()
    {
        var payment = PaidPayment(amount: 1_000_000m);
        payment.RefundAmount = 1_000_000m;
        payment.RefundStatus = "Paid";
        var booking = CharterBooking(
            departure: new DateOnly(2030, 1, 4),
            payments: [payment]);

        var summary = CharterBookingRefundSupport.BuildSummary(booking, Now);

        summary.TotalPaidAmount.ShouldBe(1_000_000m);
        summary.TotalRefundedAmount.ShouldBe(1_000_000m);
        summary.OutstandingRefundAmount.ShouldBe(0m);
        summary.CanRequestRefund.ShouldBeFalse();
    }

    [Test]
    public void BuildSummary_CannotRefund_WhenBookingCompleted()
    {
        var booking = CharterBooking(
            departure: new DateOnly(2030, 1, 4),
            payments: [PaidPayment(amount: 1_000_000m)]);
        booking.BookingStatus = BookingStatus.Completed;

        var summary = CharterBookingRefundSupport.BuildSummary(booking, Now);

        summary.CanRequestRefund.ShouldBeFalse();
    }

    [Test]
    public void GetRefundablePayments_ListsEligiblePaidPayments()
    {
        var pending = new Payment
        {
            PaymentCode = "PEND-1",
            Provider = "PayOS",
            PaymentMethod = "PayOS",
            Amount = 500_000m,
            Currency = "VND",
            PaymentPurpose = "Deposit",
            PaymentStatus = "Pending"
        };
        var booking = CharterBooking(
            departure: new DateOnly(2030, 1, 4),
            payments: [pending, PaidPayment(amount: 1_500_000m)]);

        var summary = CharterBookingRefundSupport.BuildSummary(booking, Now);
        var refundable = CharterBookingRefundSupport.GetRefundablePayments(booking, summary);

        refundable.Count.ShouldBe(1);
        refundable[0].PaidAmount.ShouldBe(1_500_000m);
        refundable[0].AvailableRefundAmount.ShouldBe(1_500_000m);
    }

    [Test]
    public void GetRefundablePayments_DistributesCapAcrossMultiplePayments()
    {
        var p1 = PaidPayment(amount: 800_000m, code: "PAY-1");
        var p2 = PaidPayment(amount: 800_000m, code: "PAY-2");
        var booking = CharterBooking(
            departure: new DateOnly(2030, 1, 4),
            payments: [p1, p2]);

        var summary = CharterBookingRefundSupport.BuildSummary(booking, Now);
        var refundable = CharterBookingRefundSupport.GetRefundablePayments(booking, summary);

        // Policy 100% × paidAmount (1.6M) = 1.6M → chia đều vì đều eligible.
        refundable.Sum(x => x.AvailableRefundAmount).ShouldBe(1_600_000m);
        refundable.All(x => x.AvailableRefundAmount > 0).ShouldBeTrue();
    }

    [Test]
    public void BuildPolicyMessage_ReturnsFriendlyText()
    {
        CharterBookingRefundSupport.BuildPolicyMessage(1.0m, TimeSpan.FromDays(5), 1_000_000m)
            .ShouldContain("100%");
        CharterBookingRefundSupport.BuildPolicyMessage(0.7m, TimeSpan.FromHours(48), 1_000_000m)
            .ShouldContain("70%");
        CharterBookingRefundSupport.BuildPolicyMessage(0m, TimeSpan.FromHours(2), 1_000_000m)
            .ShouldContain("không được hoàn");
        CharterBookingRefundSupport.BuildPolicyMessage(1.0m, TimeSpan.FromDays(5), 0m)
            .ShouldContain("chưa có thanh toán");
    }

    private static Booking CharterBooking(
        DateOnly departure,
        TimeOnly? startTime = null,
        IEnumerable<Payment>? payments = null)
    {
        var booking = new Booking
        {
            BookingType = Booking.CharterBookingType,
            BookingCode = "CB-TEST",
            BookingStatus = BookingStatus.Confirmed,
            PaymentStatus = "Paid",
            TotalAmount = 1_500_000m,
            DepartureDate = departure,
            StartTime = startTime ?? new TimeOnly(8, 0),
            RentalUnit = BoatRentalUnit.Hour,
            DurationValue = 3,
            Currency = "VND"
        };
        if (payments is not null)
        {
            foreach (var payment in payments)
            {
                booking.Payments.Add(payment);
            }
        }

        return booking;
    }

    private static Payment PaidPayment(decimal amount, string code = "PAY-1") =>
        new()
        {
            PaymentCode = code,
            Provider = "PayOS",
            PaymentMethod = "PayOS",
            Amount = amount,
            Currency = "VND",
            PaymentPurpose = "Deposit",
            PaymentStatus = "Paid",
            PaidAt = new DateTimeOffset(2029, 12, 30, 0, 0, 0, TimeSpan.Zero)
        };
}