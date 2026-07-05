namespace SaigonWaterbus.Application.Common.Interfaces;

public sealed record CharterBookingDepositPaymentRequest(
    long OrderCode,
    long Amount,
    string Description,
    string BuyerName,
    string? BuyerEmail,
    string? BuyerPhone,
    string ItemName,
    DateTimeOffset? ExpiredAt);

public sealed record CharterBookingDepositPaymentResult(
    string PaymentLinkId,
    string CheckoutUrl,
    string? QrCode,
    string Status);

public sealed record CharterBookingPaymentCancellationResult(
    string? PaymentLinkId,
    string Status,
    string? Description);

public sealed record CharterBookingPaymentStatusResult(
    long OrderCode,
    long? Amount,
    string Status,
    string? PaymentLinkId,
    string? CheckoutUrl = null);

public sealed record CharterBookingRefundPayoutRequest(
    string ReferenceId,
    long Amount,
    string Description,
    string ToBin,
    string ToAccountNumber,
    string ToAccountName,
    string IdempotencyKey);

public sealed record CharterBookingRefundPayoutResult(
    string? PayoutId,
    string Status,
    string? Description,
    string? ReferenceId = null,
    long? Amount = null);

public sealed record CharterBookingDepositPaymentWebhook(
    string Code,
    string Desc,
    bool Success,
    CharterBookingDepositPaymentWebhookData Data,
    string Signature);

public sealed record CharterBookingDepositPaymentWebhookData(
    long OrderCode,
    long Amount,
    string? Description,
    string? AccountNumber,
    string? Reference,
    string? TransactionDateTime,
    string? Currency,
    string? PaymentLinkId,
    string? Code,
    string? Desc,
    string? CounterAccountBankId,
    string? CounterAccountBankName,
    string? CounterAccountName,
    string? CounterAccountNumber,
    string? VirtualAccountName,
    string? VirtualAccountNumber);

public interface ICharterBookingPaymentGateway
{
    Task<CharterBookingDepositPaymentResult> CreateDepositPaymentAsync(
        CharterBookingDepositPaymentRequest request,
        CancellationToken cancellationToken);

    Task<CharterBookingPaymentCancellationResult> CancelPaymentAsync(
        long orderCode,
        string reason,
        CancellationToken cancellationToken);

    Task<CharterBookingPaymentStatusResult> GetPaymentAsync(
        long orderCode,
        CancellationToken cancellationToken);

    Task<CharterBookingRefundPayoutResult> CreateRefundPayoutAsync(
        CharterBookingRefundPayoutRequest request,
        CancellationToken cancellationToken);

    Task<CharterBookingRefundPayoutResult?> GetRefundPayoutByReferenceIdAsync(
        string referenceId,
        CancellationToken cancellationToken);

    bool IsValidWebhook(CharterBookingDepositPaymentWebhook webhook);
}
