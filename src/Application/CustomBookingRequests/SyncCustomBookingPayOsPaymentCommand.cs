using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Exceptions;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using ForbiddenAccessException = SaigonWaterbus.Application.Common.Exceptions.ForbiddenAccessException;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;

namespace SaigonWaterbus.Application.CustomBookingRequests;

public sealed record SyncCustomBookingPayOsPaymentCommand(long OrderCode)
    : IRequest<CustomBookingRequestDto>;

public sealed class SyncCustomBookingPayOsPaymentCommandValidator
    : AbstractValidator<SyncCustomBookingPayOsPaymentCommand>
{
    public SyncCustomBookingPayOsPaymentCommandValidator()
    {
        RuleFor(x => x.OrderCode)
            .GreaterThan(0)
            .WithMessage("orderCode thanh toán PayOS không hợp lệ.");
    }
}

public sealed class SyncCustomBookingPayOsPaymentCommandHandler
    : IRequestHandler<SyncCustomBookingPayOsPaymentCommand, CustomBookingRequestDto>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;
    private readonly TimeProvider _timeProvider;
    private readonly ICustomBookingPaymentGateway _paymentGateway;
    private readonly ICustomBookingQuoteEmailSender _quoteEmailSender;

    public SyncCustomBookingPayOsPaymentCommandHandler(
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
        SyncCustomBookingPayOsPaymentCommand request,
        CancellationToken cancellationToken)
    {
        var actor = await AuthSupport.GetCurrentUserWithRoleAsync(_context, _userContext, cancellationToken);
        var quote = await _context.Set<CustomBookingQuote>()
            .Include(x => x.CustomBookingRequest)
            .SingleOrDefaultAsync(x =>
                x.DepositPaymentOrderCode == request.OrderCode
                || x.RemainingPaymentOrderCode == request.OrderCode,
                cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy thanh toán PayOS trong hệ thống.");

        var customRequest = quote.CustomBookingRequest;
        var canManage = AuthSupport.IsAdmin(actor);
        var isOwner = AuthSupport.IsCustomer(actor) && customRequest.UserId == actor.Id;
        if (!canManage && !isOwner)
        {
            throw new ForbiddenAccessException();
        }

        CustomBookingPaymentStatusResult payment;
        try
        {
            payment = await _paymentGateway.GetPaymentAsync(request.OrderCode, cancellationToken);
        }
        catch (PaymentGatewayException ex)
        {
            throw AuthSupport.CreateValidationException("payment", ex.Message);
        }

        if (payment.OrderCode != request.OrderCode)
        {
            throw AuthSupport.CreateValidationException("payment", "PayOS trả về orderCode không khớp yêu cầu.");
        }

        var paymentKind = quote.DepositPaymentOrderCode == request.OrderCode
            ? CustomBookingPaymentKind.Deposit
            : CustomBookingPaymentKind.Remaining;
        if (!string.Equals(payment.Status, "PAID", StringComparison.OrdinalIgnoreCase))
        {
            return await LoadResultAsync(customRequest.Id, cancellationToken);
        }

        if (!payment.Amount.HasValue)
        {
            throw AuthSupport.CreateValidationException("payment", "PayOS không trả về số tiền thanh toán để đối soát.");
        }

        var wasPaid = GetPaymentStatus(quote, paymentKind) == CustomBookingDepositPaymentStatus.Paid;
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
        if (payment.Amount.Value != expectedAmount)
        {
            MarkPaymentFailed(
                quote,
                paymentKind,
                $"PayOS amount mismatch. Expected {expectedAmount}, got {payment.Amount.Value}.");
            await _context.SaveChangesAsync(cancellationToken);
            throw AuthSupport.CreateValidationException(
                "amount",
                paymentKind == CustomBookingPaymentKind.Deposit
                    ? "Số tiền PayOS không khớp tiền đặt cọc."
                    : "Số tiền PayOS không khớp tiền còn lại.");
        }

        MarkPaymentPaid(quote, paymentKind, payment.PaymentLinkId);
        if (paymentKind == CustomBookingPaymentKind.Deposit && !wasPaid)
        {
            await IncrementPromotionUsageAsync(quote, cancellationToken);
        }

        if (paymentKind == CustomBookingPaymentKind.Deposit
            && customRequest.Status == CustomBookingRequestStatus.Quoted)
        {
            customRequest.Status = CustomBookingRequestStatus.Confirmed;
            customRequest.QuoteAcceptedAt ??= _timeProvider.GetUtcNow();
        }

        await _context.SaveChangesAsync(cancellationToken);
        if (paymentKind == CustomBookingPaymentKind.Deposit && !wasPaid)
        {
            await SendPaymentConfirmationEmailAsync(customRequest.Id, cancellationToken);
        }

        return await LoadResultAsync(customRequest.Id, cancellationToken);
    }

    private async Task SendPaymentConfirmationEmailAsync(
        Guid requestId,
        CancellationToken cancellationToken)
    {
        var customRequest = await CustomBookingRequestSupport.IncludeDetails(_context.Set<CustomBookingRequest>())
            .SingleAsync(x => x.Id == requestId, cancellationToken);
        await _quoteEmailSender.SendQuoteAsync(customRequest, cancellationToken);
    }

    private async Task<CustomBookingRequestDto> LoadResultAsync(
        Guid requestId,
        CancellationToken cancellationToken)
    {
        var customRequest = await CustomBookingRequestSupport.IncludeDetails(_context.Set<CustomBookingRequest>())
            .SingleAsync(x => x.Id == requestId, cancellationToken);
        var routeSegments = await CustomBookingRequestSupport.GetMatchingRouteSegmentsAsync(
            _context,
            customRequest,
            cancellationToken);
        return CustomBookingRequestDto.From(customRequest, routeSegments);
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
