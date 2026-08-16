using SaigonWaterbus.Application.Payments;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Application.CharterBookings;

/// <summary>
/// Gom logic tính refund summary cho charter booking để FE hiển thị đúng chính sách hoàn tiền
/// và danh sách payment có thể hoàn. BE là nguồn sự thật (single source of truth) cho policy.
/// </summary>
internal static class CharterBookingRefundSupport
{
    /// <summary>
    /// Tính tổng quan refund cho booking: tổng đã thanh toán, đã hoàn, còn có thể hoàn,
    /// % chính sách theo thời điểm khởi hành, và message giải thích cho FE.
    /// Trả về <see cref="CharterBookingRefundSummary"/> để FE bind trực tiếp.
    /// </summary>
    public static CharterBookingRefundSummary BuildSummary(Booking booking, DateTimeOffset now)
    {
        var paidAmount = PaymentSupport.GetPaidAmount(booking);
        var refundedAmount = booking.Payments
            .Where(PaymentSupport.IsSettlementPayment)
            .Sum(x => x.RefundAmount);

        var departure = PaymentSupport.ResolveCharterDepartureTime(booking);
        var timeUntilDeparture = departure.HasValue
            ? departure.Value - now
            : (TimeSpan?)null;
        var policyPercent = timeUntilDeparture.HasValue
            ? PaymentSupport.ResolveRefundPercent(timeUntilDeparture.Value)
            : 0m;

        var outstandingRefundAmount = Math.Max(paidAmount - refundedAmount, 0m);

        var policyMessage = BuildPolicyMessage(policyPercent, timeUntilDeparture, paidAmount);
        var canRequestRefund = paidAmount > refundedAmount
            && booking.BookingStatus != Domain.Enums.BookingStatus.Completed
            && booking.BookingStatus != Domain.Enums.BookingStatus.Expired;

        // Khi policyPercent = 0% (huỷ dưới 24 giờ trước giờ khởi hành), chính sách không hoàn tiền
        // nhưng vẫn cho phép refund 0đ để đóng sổ booking → trạng thái Refunded.

        return new CharterBookingRefundSummary(
            paidAmount,
            refundedAmount,
            outstandingRefundAmount,
            policyPercent,
            timeUntilDeparture,
            canRequestRefund,
            policyMessage);
    }

    /// <summary>
    /// Build message giải thích policy cho FE (UI Việt).
    /// </summary>
    public static string BuildPolicyMessage(
        decimal policyPercent,
        TimeSpan? timeUntilDeparture,
        decimal paidAmount)
    {
        if (paidAmount <= 0)
        {
            return "Charter booking chưa có thanh toán nên không có số tiền để hoàn.";
        }

        return policyPercent switch
        {
            >= 1.0m => "Hủy trước giờ khởi hành ≥ 3 ngày: được hoàn 100% số tiền đã thanh toán.",
            >= 0.7m => "Hủy trước giờ khởi hành từ 24 giờ đến dưới 3 ngày: được hoàn 70% số tiền đã thanh toán.",
            _ => "Hủy dưới 24 giờ trước giờ khởi hành: không được hoàn tiền theo chính sách, nhưng vẫn có thể đóng sổ booking (refund 0đ)."
        };
    }

    /// <summary>Thời điểm khởi hành của charter booking (giờ VN) — sử dụng helper chung từ PaymentSupport.</summary>

    /// <summary>
    /// Danh sách payment có thể hoàn (đã thanh toán, còn dư để hoàn).
    /// Mỗi payment có refund amount preview theo policy.
    /// </summary>
    public static IReadOnlyList<CharterBookingRefundablePayment> GetRefundablePayments(
        Booking booking,
        CharterBookingRefundSummary summary)
    {
        var paid = booking.Payments
            .Where(x => PaymentSupport.IsSettlementPayment(x) && PaymentSupport.IsPaid(x.PaymentStatus))
            .OrderBy(x => x.Created)
            .ToList();

        if (paid.Count == 0 || !summary.CanRequestRefund)
        {
            return [];
        }

        // Policy phân bổ theo thứ tự payment cũ → mới để tránh hoàn 2 lần cùng số tiền.
        var remainingRefundable = summary.OutstandingRefundAmount;
        var cap = Math.Floor(summary.TotalPaidAmount * summary.PolicyPercent);
        var distributed = 0m;
        var result = new List<CharterBookingRefundablePayment>(paid.Count);
        foreach (var payment in paid)
        {
            var paymentOutstanding = Math.Max(payment.Amount - payment.RefundAmount, 0m);
            if (paymentOutstanding <= 0)
            {
                result.Add(new CharterBookingRefundablePayment(
                    payment.Id,
                    payment.PaymentCode,
                    payment.Amount,
                    payment.RefundAmount,
                    0m,
                    payment.PaymentStatus));
                continue;
            }

            // Số tiền có thể hoàn cho payment này = min(còn lại của payment, còn lại của tổng refundable, cap còn lại).
            var available = Math.Min(paymentOutstanding, remainingRefundable);
            available = Math.Min(available, cap - distributed);
            if (available < 0) available = 0;
            distributed += available;

            result.Add(new CharterBookingRefundablePayment(
                payment.Id,
                payment.PaymentCode,
                payment.Amount,
                payment.RefundAmount,
                available,
                payment.PaymentStatus));
        }

        return result;
    }
}

/// <summary>Summary về refund để FE hiển thị ở trang chi tiết charter booking.</summary>
public sealed record CharterBookingRefundSummary(
    decimal TotalPaidAmount,
    decimal TotalRefundedAmount,
    decimal OutstandingRefundAmount,
    decimal PolicyPercent,
    TimeSpan? TimeUntilDeparture,
    bool CanRequestRefund,
    string PolicyMessage);

/// <summary>Một payment có thể hoàn — FE sẽ list ra để user chọn payment muốn hoàn.</summary>
public sealed record CharterBookingRefundablePayment(
    Guid PaymentId,
    string PaymentCode,
    decimal PaidAmount,
    decimal AlreadyRefundedAmount,
    decimal AvailableRefundAmount,
    string PaymentStatus);