using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Application.CustomBookingRequests;

internal static class CustomBookingRefundPayoutStatus
{
    public static bool IsAccepted(CustomBookingQuote quote) =>
        IsAccepted(quote.RefundStatus, quote.RefundPayoutId, quote.RefundReferenceId);

    public static bool IsAccepted(
        string? refundStatus,
        string? payoutId,
        string? referenceId)
    {
        if (string.IsNullOrWhiteSpace(referenceId)
            || IsFailed(refundStatus))
        {
            return false;
        }

        if (string.Equals(refundStatus, "Pending", StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrWhiteSpace(payoutId))
        {
            return false;
        }

        return !string.IsNullOrWhiteSpace(payoutId)
            || !string.IsNullOrWhiteSpace(refundStatus);
    }

    public static bool IsFailed(string? refundStatus) =>
        string.Equals(refundStatus, "Failed", StringComparison.OrdinalIgnoreCase)
        || string.Equals(refundStatus, "Rejected", StringComparison.OrdinalIgnoreCase)
        || string.Equals(refundStatus, "Cancelled", StringComparison.OrdinalIgnoreCase)
        || string.Equals(refundStatus, "Canceled", StringComparison.OrdinalIgnoreCase)
        || string.Equals(refundStatus, "Error", StringComparison.OrdinalIgnoreCase);

    public static string CreateNotAcceptedReason(string? refundStatus, string? description) =>
        description
        ?? $"PayOS trả về trạng thái hoàn tiền chưa được chấp nhận: {refundStatus}.";
}
