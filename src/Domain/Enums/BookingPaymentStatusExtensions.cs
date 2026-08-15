namespace SaigonWaterbus.Domain.Enums;

public static class BookingPaymentStatusExtensions
{
    public const string UnpaidValue = "Unpaid";
    public const string PaidValue = "Paid";
    public const string RefundedValue = "Refunded";
    public const string PartiallyRefundedValue = "PartiallyRefunded"; // legacy: tồn tại trong DB cũ, không còn dùng.
    public const string DepositPaidValue = "DepositPaid";
    public const string FailedValue = "Failed";

    public static string ToDbValue(this BookingPaymentStatus status) => status switch
    {
        BookingPaymentStatus.Unpaid => UnpaidValue,
        BookingPaymentStatus.Paid => PaidValue,
        BookingPaymentStatus.Refunded => RefundedValue,
        BookingPaymentStatus.DepositPaid => DepositPaidValue,
        BookingPaymentStatus.Failed => FailedValue,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown BookingPaymentStatus.")
    };

    public static BookingPaymentStatus ToBookingPaymentStatus(this string? value) =>
        ToBookingPaymentStatus(value, throwOnUnknown: false);

    public static BookingPaymentStatus ToBookingPaymentStatus(this string? value, bool throwOnUnknown)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return BookingPaymentStatus.Unpaid;
        }

        var trimmed = value.Trim();
        var matched = trimmed.ToUpperInvariant() switch
        {
            "UNPAID" => BookingPaymentStatus.Unpaid,
            "PAID" => BookingPaymentStatus.Paid,
            "REFUNDED" => BookingPaymentStatus.Refunded,
            // "PartiallyRefunded" đã bị xóa khỏi enum — map về Paid để tương thích DB cũ.
            "PARTIALLYREFUNDED" => BookingPaymentStatus.Paid,
            "DEPOSITPAID" => BookingPaymentStatus.DepositPaid,
            "FAILED" => BookingPaymentStatus.Failed,
            _ => (BookingPaymentStatus?)null
        };

        if (matched.HasValue)
        {
            return matched.Value;
        }

        if (throwOnUnknown)
        {
            throw new ArgumentException(
                $"Unknown BookingPaymentStatus value '{value}'.",
                nameof(value));
        }

        return BookingPaymentStatus.Unpaid;
    }

    public static bool IsPaid(this BookingPaymentStatus status) =>
        status == BookingPaymentStatus.Paid
        || status == BookingPaymentStatus.DepositPaid;

    public static bool IsFailed(this BookingPaymentStatus status) =>
        status == BookingPaymentStatus.Failed;

    /// <summary>Đã hoàn tiền đủ (booking.PaymentStatus = "Refunded"). Booking tương ứng cũng đã chuyển sang <see cref="BookingStatus.Cancelled"/>.</summary>
    public static bool IsRefunded(this BookingPaymentStatus status) =>
        status == BookingPaymentStatus.Refunded;

    /// <summary>Đã hoàn tiền (chỉ check "Refunded" đủ — partial refund không còn được track ở booking-level).</summary>
    public static bool HasAnyRefund(this BookingPaymentStatus status) =>
        status == BookingPaymentStatus.Refunded;

    public static bool HasAnyRefund(this string? value) =>
        value.ToBookingPaymentStatus().HasAnyRefund();
}
