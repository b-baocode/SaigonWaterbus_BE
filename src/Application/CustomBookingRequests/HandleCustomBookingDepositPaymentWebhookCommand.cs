using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Application.CustomBookingRequests;

public sealed record CustomBookingDepositPaymentWebhookResultDto(
    bool Processed,
    long OrderCode,
    CustomBookingDepositPaymentStatus? Status,
    string Message);

public sealed record HandleCustomBookingDepositPaymentWebhookCommand(
    CustomBookingDepositPaymentWebhook Webhook)
    : IRequest<CustomBookingDepositPaymentWebhookResultDto>;

public sealed class HandleCustomBookingDepositPaymentWebhookCommandHandler
    : IRequestHandler<HandleCustomBookingDepositPaymentWebhookCommand, CustomBookingDepositPaymentWebhookResultDto>
{
    private readonly IApplicationDbContext _context;
    private readonly ICustomBookingPaymentGateway _paymentGateway;
    private readonly TimeProvider _timeProvider;

    public HandleCustomBookingDepositPaymentWebhookCommandHandler(
        IApplicationDbContext context,
        ICustomBookingPaymentGateway paymentGateway,
        TimeProvider timeProvider)
    {
        _context = context;
        _paymentGateway = paymentGateway;
        _timeProvider = timeProvider;
    }

    public async Task<CustomBookingDepositPaymentWebhookResultDto> Handle(
        HandleCustomBookingDepositPaymentWebhookCommand request,
        CancellationToken cancellationToken)
    {
        var webhook = request.Webhook;
        if (!_paymentGateway.IsValidWebhook(webhook))
        {
            throw AuthSupport.CreateValidationException("signature", "Chữ ký webhook PayOS không hợp lệ.");
        }

        var quote = await _context.Set<CustomBookingQuote>()
            .Include(x => x.CustomBookingRequest)
            .SingleOrDefaultAsync(x =>
                x.DepositPaymentOrderCode == webhook.Data.OrderCode
                || x.RemainingPaymentOrderCode == webhook.Data.OrderCode,
                cancellationToken);

        if (quote is null)
        {
            return new CustomBookingDepositPaymentWebhookResultDto(
                false,
                webhook.Data.OrderCode,
                null,
                "Không tìm thấy orderCode trong hệ thống.");
        }

        var isPaid = webhook.Success
            && string.Equals(webhook.Code, "00", StringComparison.Ordinal)
            && string.Equals(webhook.Data.Code, "00", StringComparison.Ordinal);

        var paymentKind = quote.DepositPaymentOrderCode == webhook.Data.OrderCode
            ? CustomBookingPaymentKind.Deposit
            : CustomBookingPaymentKind.Remaining;
        var wasPaid = GetPaymentStatus(quote, paymentKind) == CustomBookingDepositPaymentStatus.Paid;

        if (isPaid)
        {
            var expectedAmount = CustomBookingPaymentSupport.ToPayOsAmount(
                paymentKind == CustomBookingPaymentKind.Deposit
                    ? quote.DepositAmount
                    : quote.RemainingAmount,
                paymentKind == CustomBookingPaymentKind.Deposit
                    ? nameof(quote.DepositAmount)
                    : nameof(quote.RemainingAmount),
                paymentKind == CustomBookingPaymentKind.Deposit
                    ? "Số tiền đặt cọc phải là số nguyên VND lớn hơn 0."
                    : "Số tiền còn lại phải là số nguyên VND lớn hơn 0.");
            if (webhook.Data.Amount != expectedAmount)
            {
                MarkPaymentFailed(
                    quote,
                    paymentKind,
                    $"PayOS amount mismatch. Expected {expectedAmount}, got {webhook.Data.Amount}.");
                await _context.SaveChangesAsync(cancellationToken);

                throw AuthSupport.CreateValidationException(
                    "amount",
                    paymentKind == CustomBookingPaymentKind.Deposit
                        ? "Số tiền webhook PayOS không khớp tiền đặt cọc."
                        : "Số tiền webhook PayOS không khớp tiền còn lại.");
            }

            MarkPaymentPaid(quote, paymentKind, webhook.Data.PaymentLinkId);
            if (paymentKind == CustomBookingPaymentKind.Deposit && !wasPaid)
            {
                await IncrementPromotionUsageAsync(quote, cancellationToken);
            }

            if (paymentKind == CustomBookingPaymentKind.Deposit
                && quote.CustomBookingRequest.Status == CustomBookingRequestStatus.Quoted)
            {
                quote.CustomBookingRequest.Status = CustomBookingRequestStatus.Confirmed;
                quote.CustomBookingRequest.QuoteAcceptedAt ??= _timeProvider.GetUtcNow();
            }

            await _context.SaveChangesAsync(cancellationToken);
            return new CustomBookingDepositPaymentWebhookResultDto(
                true,
                webhook.Data.OrderCode,
                paymentKind == CustomBookingPaymentKind.Deposit
                    ? quote.DepositPaymentStatus
                    : quote.RemainingPaymentStatus,
                paymentKind == CustomBookingPaymentKind.Deposit
                    ? "Đã ghi nhận thanh toán đặt cọc."
                    : "Đã ghi nhận thanh toán phần còn lại.");
        }

        if (GetPaymentStatus(quote, paymentKind) != CustomBookingDepositPaymentStatus.Paid)
        {
            MarkPaymentFailed(quote, paymentKind, webhook.Data.Desc ?? webhook.Desc);
            await _context.SaveChangesAsync(cancellationToken);
        }

        return new CustomBookingDepositPaymentWebhookResultDto(
            true,
            webhook.Data.OrderCode,
            GetPaymentStatus(quote, paymentKind),
            "Webhook PayOS không phải trạng thái thanh toán thành công.");
    }

    private void MarkPaymentPaid(
        CustomBookingQuote quote,
        CustomBookingPaymentKind paymentKind,
        string? paymentLinkId)
    {
        var now = _timeProvider.GetUtcNow();
        if (paymentKind == CustomBookingPaymentKind.Deposit)
        {
            quote.DepositPaymentStatus = CustomBookingDepositPaymentStatus.Paid;
            quote.DepositPaymentPaidAt ??= now;
            quote.DepositPaymentFailureReason = null;
            if (string.IsNullOrWhiteSpace(quote.DepositPaymentLinkId))
            {
                quote.DepositPaymentLinkId = paymentLinkId;
            }

            return;
        }

        quote.RemainingPaymentStatus = CustomBookingDepositPaymentStatus.Paid;
        quote.RemainingPaymentPaidAt ??= now;
        quote.RemainingPaymentFailureReason = null;
        if (string.IsNullOrWhiteSpace(quote.RemainingPaymentLinkId))
        {
            quote.RemainingPaymentLinkId = paymentLinkId;
        }
    }

    private static void MarkPaymentFailed(
        CustomBookingQuote quote,
        CustomBookingPaymentKind paymentKind,
        string? reason)
    {
        if (paymentKind == CustomBookingPaymentKind.Deposit)
        {
            quote.DepositPaymentStatus = CustomBookingDepositPaymentStatus.Failed;
            quote.DepositPaymentFailureReason = reason;
            return;
        }

        quote.RemainingPaymentStatus = CustomBookingDepositPaymentStatus.Failed;
        quote.RemainingPaymentFailureReason = reason;
    }

    private static CustomBookingDepositPaymentStatus GetPaymentStatus(
        CustomBookingQuote quote,
        CustomBookingPaymentKind paymentKind) =>
        paymentKind == CustomBookingPaymentKind.Deposit
            ? quote.DepositPaymentStatus
            : quote.RemainingPaymentStatus;

    private async Task IncrementPromotionUsageAsync(
        CustomBookingQuote quote,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(quote.DiscountCode))
        {
            return;
        }

        var promotion = await _context.Set<Promotion>()
            .SingleOrDefaultAsync(x => x.PromotionCode == quote.DiscountCode, cancellationToken);
        if (promotion is not null)
        {
            promotion.UsageCount++;
        }
    }

    private enum CustomBookingPaymentKind
    {
        Deposit,
        Remaining
    }
}
