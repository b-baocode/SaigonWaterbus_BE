using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Domain.Entities;

public class CustomBookingQuote : BaseGuidAuditableEntity
{
    public Guid CustomBookingRequestId { get; set; }

    public CustomBookingRequest CustomBookingRequest { get; set; } = null!;

    public decimal QuotedPrice { get; set; }

    public decimal ServiceFeeAmount { get; set; }

    public string? DiscountCode { get; set; }

    public decimal DiscountAmount { get; set; }

    public decimal DepositPercent { get; set; } = 50m;

    public decimal DepositAmount { get; set; }

    public decimal RemainingAmount { get; set; }

    public string Currency { get; set; } = "VND";

    public string? PriceNote { get; set; }

    public DateTimeOffset? ValidUntil { get; set; }

    public CustomBookingDepositPaymentStatus DepositPaymentStatus { get; set; } =
        CustomBookingDepositPaymentStatus.NotCreated;

    public long? DepositPaymentOrderCode { get; set; }

    public string? DepositPaymentLinkId { get; set; }

    public string? DepositPaymentCheckoutUrl { get; set; }

    public string? DepositPaymentQrCode { get; set; }

    public DateTimeOffset? DepositPaymentCreatedAt { get; set; }

    public DateTimeOffset? DepositPaymentPaidAt { get; set; }

    public DateTimeOffset? DepositPaymentCancelledAt { get; set; }

    public string? DepositPaymentFailureReason { get; set; }

    public CustomBookingDepositPaymentStatus RemainingPaymentStatus { get; set; } =
        CustomBookingDepositPaymentStatus.NotCreated;

    public long? RemainingPaymentOrderCode { get; set; }

    public string? RemainingPaymentLinkId { get; set; }

    public string? RemainingPaymentCheckoutUrl { get; set; }

    public string? RemainingPaymentQrCode { get; set; }

    public DateTimeOffset? RemainingPaymentCreatedAt { get; set; }

    public DateTimeOffset? RemainingPaymentPaidAt { get; set; }

    public DateTimeOffset? RemainingPaymentCancelledAt { get; set; }

    public string? RemainingPaymentFailureReason { get; set; }

    public decimal RefundEligiblePercent { get; set; }

    public decimal RefundAmount { get; set; }

    public string? RefundPolicyNote { get; set; }

    public string? RefundBankBin { get; set; }

    public string? RefundAccountNumber { get; set; }

    public string? RefundAccountName { get; set; }

    public string? RefundReferenceId { get; set; }

    public string? RefundPayoutId { get; set; }

    public string? RefundStatus { get; set; }

    public string? RefundFailureReason { get; set; }

    public DateTimeOffset? RefundRequestedAt { get; set; }

    public DateTimeOffset? RefundProcessedAt { get; set; }
}
