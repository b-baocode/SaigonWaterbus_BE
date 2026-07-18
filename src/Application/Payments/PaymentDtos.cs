using System.Text.Json.Serialization;

namespace SaigonWaterbus.Application.Payments;

public enum BookingPaymentOption
{
    Deposit = 0,
    Full = 1,
    Remaining = 2
}

public sealed record CreatePaymentRequest(
    Guid BookingId,
    BookingPaymentOption PaymentOption = BookingPaymentOption.Full,
    decimal? DepositPercent = null,
    string? PromotionCode = null,
    int? PointsToUse = null);

public sealed record PaymentDto(
    Guid PaymentId,
    Guid BookingId,
    string BookingCode,
    string PaymentCode,
    string? Provider,
    string? ProviderTransactionId,
    decimal Amount,
    string Currency,
    string PaymentMethod,
    string PaymentPurpose,
    string PaymentStatus,
    string BookingStatus,
    string BookingPaymentStatus,
    decimal BookingDepositAmount,
    decimal BookingRemainingAmount,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? CheckoutUrl,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? QrCode,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    DateTimeOffset? PaidAt,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    DateTimeOffset? ExpiresAt,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    DateTimeOffset? BookingHoldExpiresAt,
    decimal RefundAmount,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    decimal? RefundRequestedAmount,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? RefundMethod,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? RefundReason,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? RefundReferenceId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? RefundPayoutId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? RefundStatus,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? RefundFailureReason,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    Guid? RefundProcessedByUserId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    DateTimeOffset? RefundedAt);

public sealed record RefundPaymentRequest(
    string Reason,
    string BankBin,
    string AccountNumber,
    string AccountName);

public sealed record ManualRefundPaymentRequest(
    string Reason,
    string? ReferenceId = null,
    string? PayoutId = null,
    DateTimeOffset? RefundedAt = null);

public sealed record PaymentWebhookResult(
    bool Processed,
    long OrderCode,
    string? PaymentStatus,
    string Message);
