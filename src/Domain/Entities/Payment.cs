using SaigonWaterbus.Domain.Common;

namespace SaigonWaterbus.Domain.Entities;

public class Payment : BaseGuidAuditableEntity
{
    public Guid BookingId { get; set; }
    public string PaymentCode { get; set; } = null!;
    public string? Provider { get; set; }
    public string? ProviderTransactionId { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "VND";
    public string PaymentMethod { get; set; } = "Cash";
    public string PaymentPurpose { get; set; } = "Full";
    public string PaymentStatus { get; set; } = "Pending";
    public string? CheckoutUrl { get; set; }
    public string? QrCode { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset? PaidAt { get; set; }
    public decimal RefundAmount { get; set; }
    public decimal? RefundRequestedAmount { get; set; }
    public string? RefundMethod { get; set; }
    public string? RefundReason { get; set; }
    public string? RefundReferenceId { get; set; }
    public string? RefundPayoutId { get; set; }
    public string? RefundStatus { get; set; }
    public string? RefundFailureReason { get; set; }
    public Guid? RefundProcessedByUserId { get; set; }
    public DateTimeOffset? RefundedAt { get; set; }

    // Admin mở lại refund cho customer tự nhập STK — tracking "1 lần duy nhất".
    public int CustomerRefundAttempts { get; set; }
    public DateTimeOffset? RefundReleasedAt { get; set; }
    public Guid? RefundReleasedByUserId { get; set; }
    public string? RefundReleasedReason { get; set; }

    public Booking Booking { get; set; } = null!;
}
