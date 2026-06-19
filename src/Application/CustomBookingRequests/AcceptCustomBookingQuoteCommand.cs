using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Exceptions;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using ForbiddenAccessException = SaigonWaterbus.Application.Common.Exceptions.ForbiddenAccessException;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;

namespace SaigonWaterbus.Application.CustomBookingRequests;

public enum CustomBookingQuotePaymentOption
{
    Deposit = 0,
    Full = 1
}

public sealed record AcceptCustomBookingQuoteCommand(
    Guid Id,
    CustomBookingQuotePaymentOption PaymentOption = CustomBookingQuotePaymentOption.Deposit,
    string? DiscountCode = null)
    : IRequest<CustomBookingRequestDto>;

public sealed class AcceptCustomBookingQuoteCommandHandler
    : IRequestHandler<AcceptCustomBookingQuoteCommand, CustomBookingRequestDto>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;
    private readonly TimeProvider _timeProvider;
    private readonly ICustomBookingPaymentGateway _paymentGateway;
    private readonly ICustomBookingQuoteEmailSender _quoteEmailSender;

    public AcceptCustomBookingQuoteCommandHandler(
        IApplicationDbContext context,
        IUserContext userContext,
        TimeProvider timeProvider,
        ICustomBookingPaymentGateway paymentGateway,
        ICustomBookingQuoteEmailSender quoteEmailSender)
    {
        _context = context;
        _userContext = userContext;
        _timeProvider = timeProvider;
        _paymentGateway = paymentGateway;
        _quoteEmailSender = quoteEmailSender;
    }

    public async Task<CustomBookingRequestDto> Handle(
        AcceptCustomBookingQuoteCommand request,
        CancellationToken cancellationToken)
    {
        var actor = await AuthSupport.GetCurrentUserWithRoleAsync(_context, _userContext, cancellationToken);
        var customRequest = await CustomBookingRequestSupport.IncludeDetails(_context.Set<CustomBookingRequest>())
            .SingleOrDefaultAsync(x => x.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy yêu cầu thuê tàu.");

        if (customRequest.UserId != actor.Id)
        {
            throw new ForbiddenAccessException();
        }

        var now = _timeProvider.GetUtcNow();
        CustomBookingRequestSupport.EnsureCanAcceptQuote(customRequest, now);
        CustomBookingRequestSupport.EnsureVesselMatchesRequest(customRequest, customRequest.AssignedVessel!);
        var routeSegments = await CustomBookingRequestSupport.GetMatchingRouteSegmentsAsync(
            _context,
            customRequest,
            cancellationToken);
        CustomBookingRequestSupport.ApplyRouteEstimate(customRequest, routeSegments);
        await CustomBookingAvailability.EnsureVesselAvailableAsync(
            _context,
            customRequest,
            customRequest.AssignedVesselId!.Value,
            cancellationToken);

        var quote = customRequest.Quote!;
        var requestedDiscountCode = NormalizeDiscountCode(request.DiscountCode);
        if (quote.DepositPaymentStatus == CustomBookingDepositPaymentStatus.Paid)
        {
            throw AuthSupport.CreateValidationException("payment", "Booking này đã được thanh toán đặt cọc.");
        }

        if (quote.DepositPaymentStatus == CustomBookingDepositPaymentStatus.Pending
            && !string.IsNullOrWhiteSpace(quote.DepositPaymentCheckoutUrl))
        {
            if (!string.IsNullOrWhiteSpace(requestedDiscountCode)
                && !string.Equals(requestedDiscountCode, NormalizeDiscountCode(quote.DiscountCode), StringComparison.Ordinal))
            {
                throw AuthSupport.CreateValidationException(
                    nameof(request.DiscountCode),
                    "Booking đã có link thanh toán đang chờ xử lý, không thể đổi mã giảm giá trên link hiện tại.");
            }

            return CustomBookingRequestDto.From(customRequest, routeSegments);
        }

        if (!string.IsNullOrWhiteSpace(requestedDiscountCode))
        {
            await ApplyCustomerDiscountAsync(quote, requestedDiscountCode, now, cancellationToken);
        }

        var payFullAmount = request.PaymentOption == CustomBookingQuotePaymentOption.Full;
        if (payFullAmount)
        {
            quote.DepositPercent = 100m;
            quote.DepositAmount = quote.QuotedPrice;
            quote.RemainingAmount = 0m;
        }

        if (quote.RemainingAmount > 0m
            && now >= CustomBookingPaymentSupport.ResolveRemainingPaymentDeadline(customRequest))
        {
            throw AuthSupport.CreateValidationException(
                "payment",
                "Booking đã quá hạn thanh toán phần còn lại trước 24 giờ khởi hành. Vui lòng liên hệ Admin để báo giá thanh toán 100%.");
        }

        var orderCode = await CustomBookingPaymentSupport.GeneratePaymentOrderCodeAsync(
            _context,
            now,
            cancellationToken);
        var amount = CustomBookingPaymentSupport.ToPayOsAmount(
            quote.DepositAmount,
            nameof(quote.DepositAmount),
            "Số tiền đặt cọc phải là số nguyên VND lớn hơn 0.");
        var paymentExpiredAt = ResolvePaymentExpiredAt(quote.ValidUntil, customRequest);
        customRequest.Status = CustomBookingRequestStatus.Quoted;
        customRequest.StatusReason = null;
        customRequest.QuoteAcceptedAt = null;
        quote.DepositPaymentStatus = CustomBookingDepositPaymentStatus.Pending;
        quote.DepositPaymentOrderCode = orderCode;
        quote.DepositPaymentLinkId = null;
        quote.DepositPaymentCheckoutUrl = null;
        quote.DepositPaymentQrCode = null;
        quote.DepositPaymentCreatedAt = now;
        quote.DepositPaymentPaidAt = null;
        quote.DepositPaymentCancelledAt = null;
        quote.DepositPaymentFailureReason = null;
        await _context.SaveChangesAsync(cancellationToken);

        CustomBookingDepositPaymentResult paymentResult;
        try
        {
            paymentResult = await _paymentGateway.CreateDepositPaymentAsync(
                new CustomBookingDepositPaymentRequest(
                    orderCode,
                    amount,
                    CustomBookingPaymentSupport.CreatePaymentDescription(customRequest),
                    customRequest.ContactName,
                    customRequest.ContactEmail,
                    customRequest.ContactPhone,
                    payFullAmount || quote.RemainingAmount <= 0m
                        ? $"Full payment {CustomBookingPaymentSupport.CreateBookingReference(customRequest)}"
                        : $"Deposit booking {CustomBookingPaymentSupport.CreateBookingReference(customRequest)}",
                    paymentExpiredAt),
                cancellationToken);
        }
        catch (PaymentGatewayException ex)
        {
            var recovered = await TryRecoverCreatedPaymentAsync(
                quote,
                orderCode,
                amount,
                cancellationToken);
            if (recovered)
            {
                return CustomBookingRequestDto.From(customRequest, routeSegments);
            }

            quote.DepositPaymentStatus = CustomBookingDepositPaymentStatus.Failed;
            quote.DepositPaymentFailureReason = ex.Message;
            await _context.SaveChangesAsync(cancellationToken);
            throw AuthSupport.CreateValidationException("payment", ex.Message);
        }

        quote.DepositPaymentLinkId = paymentResult.PaymentLinkId;
        quote.DepositPaymentCheckoutUrl = paymentResult.CheckoutUrl;
        quote.DepositPaymentQrCode = paymentResult.QrCode;
        quote.DepositPaymentFailureReason = null;

        await _context.SaveChangesAsync(cancellationToken);

        return CustomBookingRequestDto.From(customRequest, routeSegments);
    }

    private static DateTimeOffset? ResolvePaymentExpiredAt(
        DateTimeOffset? quoteValidUntil,
        CustomBookingRequest customRequest)
    {
        var departureAt = CustomBookingRefundPolicy.GetDepartureAtOrThrow(customRequest);
        if (!quoteValidUntil.HasValue)
        {
            return departureAt;
        }

        return quoteValidUntil.Value <= departureAt
            ? quoteValidUntil.Value
            : departureAt;
    }

    private async Task ApplyCustomerDiscountAsync(
        CustomBookingQuote quote,
        string discountCode,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var grossPrice = quote.QuotedPrice + quote.DiscountAmount;
        var discount = await ResolveDiscountAsync(discountCode, grossPrice, now, cancellationToken);
        var quotedPrice = grossPrice - discount.Amount;
        if (quotedPrice <= 0)
        {
            throw AuthSupport.CreateValidationException(
                nameof(discountCode),
                "Mã giảm giá không được làm tổng tiền sau giảm nhỏ hơn hoặc bằng 0.");
        }

        var depositAmount = decimal.Round(
            quotedPrice * quote.DepositPercent / 100m,
            0,
            MidpointRounding.AwayFromZero);
        if (depositAmount <= 0)
        {
            throw AuthSupport.CreateValidationException(
                nameof(discountCode),
                "Tiền đặt cọc sau giảm giá phải lớn hơn 0 VND.");
        }

        quote.DiscountCode = discount.Code;
        quote.DiscountAmount = discount.Amount;
        quote.QuotedPrice = quotedPrice;
        quote.DepositAmount = depositAmount;
        quote.RemainingAmount = quotedPrice - depositAmount;
    }

    private async Task<(string Code, decimal Amount)> ResolveDiscountAsync(
        string discountCode,
        decimal subtotalAmount,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var code = discountCode.Trim().ToUpperInvariant();
        var promotion = await _context.Set<Promotion>()
            .SingleOrDefaultAsync(x => x.PromotionCode == code, cancellationToken)
            ?? throw AuthSupport.CreateValidationException(nameof(discountCode), "Mã giảm giá không tồn tại.");

        if (!string.Equals(promotion.Status, "Active", StringComparison.OrdinalIgnoreCase))
        {
            throw AuthSupport.CreateValidationException(nameof(discountCode), "Mã giảm giá không hoạt động.");
        }

        if (promotion.ValidFrom > now || promotion.ValidTo < now)
        {
            throw AuthSupport.CreateValidationException(nameof(discountCode), "Mã giảm giá không còn hiệu lực.");
        }

        if (promotion.UsageLimit.HasValue && promotion.UsageCount >= promotion.UsageLimit.Value)
        {
            throw AuthSupport.CreateValidationException(nameof(discountCode), "Mã giảm giá đã hết lượt sử dụng.");
        }

        if (promotion.MinOrderValue.HasValue && subtotalAmount < promotion.MinOrderValue.Value)
        {
            throw AuthSupport.CreateValidationException(nameof(discountCode), "Giá trị đơn hàng chưa đủ điều kiện dùng mã giảm giá.");
        }

        var discountAmount = promotion.PromotionType == PromotionType.Percent
            ? decimal.Round(subtotalAmount * promotion.DiscountValue / 100m, 0, MidpointRounding.AwayFromZero)
            : decimal.Round(Math.Min(promotion.DiscountValue, subtotalAmount), 0, MidpointRounding.AwayFromZero);

        return (code, discountAmount);
    }

    private static string? NormalizeDiscountCode(string? discountCode) =>
        string.IsNullOrWhiteSpace(discountCode)
            ? null
            : discountCode.Trim().ToUpperInvariant();

    private async Task<bool> TryRecoverCreatedPaymentAsync(
        CustomBookingQuote quote,
        long orderCode,
        long expectedAmount,
        CancellationToken cancellationToken)
    {
        try
        {
            var payment = await _paymentGateway.GetPaymentAsync(orderCode, cancellationToken);
            if (payment.OrderCode != orderCode
                || !payment.Amount.HasValue
                || payment.Amount.Value != expectedAmount)
            {
                return false;
            }

            quote.DepositPaymentStatus = ResolveRecoveredPaymentStatus(payment.Status);
            quote.DepositPaymentLinkId = payment.PaymentLinkId;
            quote.DepositPaymentCheckoutUrl = payment.CheckoutUrl;
            quote.DepositPaymentFailureReason = null;
            if (quote.DepositPaymentStatus == CustomBookingDepositPaymentStatus.Paid)
            {
                quote.DepositPaymentPaidAt ??= _timeProvider.GetUtcNow();
                quote.CustomBookingRequest.Status = CustomBookingRequestStatus.Confirmed;
                quote.CustomBookingRequest.QuoteAcceptedAt ??= _timeProvider.GetUtcNow();
                await IncrementPromotionUsageAsync(quote, cancellationToken);
            }
            else if (quote.DepositPaymentStatus == CustomBookingDepositPaymentStatus.Cancelled)
            {
                quote.DepositPaymentCancelledAt ??= _timeProvider.GetUtcNow();
            }

            await _context.SaveChangesAsync(cancellationToken);
            if (quote.DepositPaymentStatus == CustomBookingDepositPaymentStatus.Paid)
            {
                await SendPaymentConfirmationEmailAsync(quote.CustomBookingRequest.Id, cancellationToken);
            }

            return true;
        }
        catch (PaymentGatewayException)
        {
            return false;
        }
    }

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

    private async Task SendPaymentConfirmationEmailAsync(
        Guid requestId,
        CancellationToken cancellationToken)
    {
        var customRequest = await CustomBookingRequestSupport.IncludeDetails(_context.Set<CustomBookingRequest>())
            .SingleAsync(x => x.Id == requestId, cancellationToken);
        await _quoteEmailSender.SendQuoteAsync(customRequest, cancellationToken);
    }

    private static CustomBookingDepositPaymentStatus ResolveRecoveredPaymentStatus(string status)
    {
        if (string.Equals(status, "PAID", StringComparison.OrdinalIgnoreCase))
        {
            return CustomBookingDepositPaymentStatus.Paid;
        }

        if (string.Equals(status, "CANCELLED", StringComparison.OrdinalIgnoreCase))
        {
            return CustomBookingDepositPaymentStatus.Cancelled;
        }

        if (string.Equals(status, "EXPIRED", StringComparison.OrdinalIgnoreCase))
        {
            return CustomBookingDepositPaymentStatus.Expired;
        }

        return CustomBookingDepositPaymentStatus.Pending;
    }
}
