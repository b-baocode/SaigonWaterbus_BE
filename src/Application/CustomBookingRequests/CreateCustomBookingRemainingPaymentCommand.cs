using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Exceptions;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using ForbiddenAccessException = SaigonWaterbus.Application.Common.Exceptions.ForbiddenAccessException;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;

namespace SaigonWaterbus.Application.CustomBookingRequests;

public sealed record CreateCustomBookingRemainingPaymentCommand(Guid Id) : IRequest<CustomBookingRequestDto>;

public sealed class CreateCustomBookingRemainingPaymentCommandHandler
    : IRequestHandler<CreateCustomBookingRemainingPaymentCommand, CustomBookingRequestDto>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;
    private readonly TimeProvider _timeProvider;
    private readonly ICustomBookingPaymentGateway _paymentGateway;

    public CreateCustomBookingRemainingPaymentCommandHandler(
        IApplicationDbContext context,
        IUserContext userContext,
        TimeProvider timeProvider,
        ICustomBookingPaymentGateway paymentGateway)
    {
        _context = context;
        _userContext = userContext;
        _timeProvider = timeProvider;
        _paymentGateway = paymentGateway;
    }

    public async Task<CustomBookingRequestDto> Handle(
        CreateCustomBookingRemainingPaymentCommand request,
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

        var quote = customRequest.Quote
            ?? throw AuthSupport.CreateValidationException(nameof(customRequest.Quote), "Booking chưa có báo giá.");

        if (customRequest.Status != CustomBookingRequestStatus.Confirmed
            || quote.DepositPaymentStatus != CustomBookingDepositPaymentStatus.Paid)
        {
            throw AuthSupport.CreateValidationException(
                nameof(customRequest.Status),
                "Chỉ tạo thanh toán phần còn lại sau khi PayOS xác nhận tiền cọc.");
        }

        if (quote.RemainingAmount <= 0)
        {
            throw AuthSupport.CreateValidationException(
                nameof(quote.RemainingAmount),
                "Booking không còn số tiền cần thanh toán.");
        }

        if (quote.RemainingPaymentStatus == CustomBookingDepositPaymentStatus.Paid)
        {
            throw AuthSupport.CreateValidationException("payment", "Booking này đã thanh toán đủ.");
        }

        if (quote.RemainingPaymentStatus == CustomBookingDepositPaymentStatus.Pending
            && !string.IsNullOrWhiteSpace(quote.RemainingPaymentCheckoutUrl))
        {
            var existingRouteSegments = await CustomBookingRequestSupport.GetMatchingRouteSegmentsAsync(
                _context,
                customRequest,
                cancellationToken);
            return CustomBookingRequestDto.From(customRequest, existingRouteSegments);
        }

        var now = _timeProvider.GetUtcNow();
        var paymentExpiredAt = CustomBookingPaymentSupport.ResolveRemainingPaymentDeadline(customRequest);
        if (now >= paymentExpiredAt)
        {
            throw AuthSupport.CreateValidationException(
                "remainingPayment",
                "Đã quá hạn thanh toán phần còn lại. Vui lòng liên hệ Admin để xử lý booking.");
        }

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

        var orderCode = await CustomBookingPaymentSupport.GeneratePaymentOrderCodeAsync(
            _context,
            now,
            cancellationToken);
        var amount = CustomBookingPaymentSupport.ToPayOsAmount(
            quote.RemainingAmount,
            nameof(quote.RemainingAmount),
            "Số tiền còn lại phải là số nguyên VND lớn hơn 0.");

        quote.RemainingPaymentStatus = CustomBookingDepositPaymentStatus.Pending;
        quote.RemainingPaymentOrderCode = orderCode;
        quote.RemainingPaymentLinkId = null;
        quote.RemainingPaymentCheckoutUrl = null;
        quote.RemainingPaymentQrCode = null;
        quote.RemainingPaymentCreatedAt = now;
        quote.RemainingPaymentPaidAt = null;
        quote.RemainingPaymentCancelledAt = null;
        quote.RemainingPaymentFailureReason = null;
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
                    $"Balance booking {CustomBookingPaymentSupport.CreateBookingReference(customRequest)}",
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

            quote.RemainingPaymentStatus = CustomBookingDepositPaymentStatus.Failed;
            quote.RemainingPaymentFailureReason = ex.Message;
            await _context.SaveChangesAsync(cancellationToken);
            throw AuthSupport.CreateValidationException("payment", ex.Message);
        }

        quote.RemainingPaymentLinkId = paymentResult.PaymentLinkId;
        quote.RemainingPaymentCheckoutUrl = paymentResult.CheckoutUrl;
        quote.RemainingPaymentQrCode = paymentResult.QrCode;
        quote.RemainingPaymentFailureReason = null;

        await _context.SaveChangesAsync(cancellationToken);

        return CustomBookingRequestDto.From(customRequest, routeSegments);
    }

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

            quote.RemainingPaymentStatus = ResolveRecoveredPaymentStatus(payment.Status);
            quote.RemainingPaymentLinkId = payment.PaymentLinkId;
            quote.RemainingPaymentCheckoutUrl = payment.CheckoutUrl;
            quote.RemainingPaymentFailureReason = null;
            if (quote.RemainingPaymentStatus == CustomBookingDepositPaymentStatus.Paid)
            {
                quote.RemainingPaymentPaidAt ??= _timeProvider.GetUtcNow();
            }
            else if (quote.RemainingPaymentStatus == CustomBookingDepositPaymentStatus.Cancelled)
            {
                quote.RemainingPaymentCancelledAt ??= _timeProvider.GetUtcNow();
            }

            await _context.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (PaymentGatewayException)
        {
            return false;
        }
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
