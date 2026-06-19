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

        CustomBookingDepositPaymentResult paymentResult;
        try
        {
            paymentResult = await _paymentGateway.CreateDepositPaymentAsync(
                new CustomBookingDepositPaymentRequest(
                    orderCode,
                    amount,
                    CustomBookingPaymentSupport.CreatePaymentDescription(orderCode),
                    customRequest.ContactName,
                    customRequest.ContactEmail,
                    customRequest.ContactPhone,
                    $"Thanh toan con lai {customRequest.Id.ToString("N")[..8].ToUpperInvariant()}",
                    paymentExpiredAt),
                cancellationToken);
        }
        catch (PaymentGatewayException ex)
        {
            throw AuthSupport.CreateValidationException("payment", ex.Message);
        }

        quote.RemainingPaymentStatus = CustomBookingDepositPaymentStatus.Pending;
        quote.RemainingPaymentOrderCode = orderCode;
        quote.RemainingPaymentLinkId = paymentResult.PaymentLinkId;
        quote.RemainingPaymentCheckoutUrl = paymentResult.CheckoutUrl;
        quote.RemainingPaymentQrCode = paymentResult.QrCode;
        quote.RemainingPaymentCreatedAt = now;
        quote.RemainingPaymentPaidAt = null;
        quote.RemainingPaymentCancelledAt = null;
        quote.RemainingPaymentFailureReason = null;

        await _context.SaveChangesAsync(cancellationToken);

        return CustomBookingRequestDto.From(customRequest, routeSegments);
    }
}
