namespace SaigonWaterbus.Application.Common.Interfaces;

public sealed record CustomBookingDepositPaymentRequest(
    long OrderCode,
    long Amount,
    string Description,
    string BuyerName,
    string? BuyerEmail,
    string? BuyerPhone,
    string ItemName,
    DateTimeOffset? ExpiredAt);

public sealed record CustomBookingDepositPaymentResult(
    string PaymentLinkId,
    string CheckoutUrl,
    string? QrCode,
    string Status);

public sealed record CustomBookingPaymentCancellationResult(
    string? PaymentLinkId,
    string Status,
    string? Description);

public sealed record CustomBookingRefundPayoutRequest(
    string ReferenceId,
    long Amount,
    string Description,
    string ToBin,
    string ToAccountNumber,
    string ToAccountName,
    string IdempotencyKey);

public sealed record CustomBookingRefundPayoutResult(
    string? PayoutId,
    string Status,
    string? Description);

public sealed record CustomBookingDepositPaymentWebhook(
    string Code,
    string Desc,
    bool Success,
    CustomBookingDepositPaymentWebhookData Data,
    string Signature);

public sealed record CustomBookingDepositPaymentWebhookData(
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

public interface ICustomBookingPaymentGateway
{
    Task<CustomBookingDepositPaymentResult> CreateDepositPaymentAsync(
        CustomBookingDepositPaymentRequest request,
        CancellationToken cancellationToken);

    Task<CustomBookingPaymentCancellationResult> CancelPaymentAsync(
        long orderCode,
        string reason,
        CancellationToken cancellationToken);

    Task<CustomBookingRefundPayoutResult> CreateRefundPayoutAsync(
        CustomBookingRefundPayoutRequest request,
        CancellationToken cancellationToken);

    bool IsValidWebhook(CustomBookingDepositPaymentWebhook webhook);
}
