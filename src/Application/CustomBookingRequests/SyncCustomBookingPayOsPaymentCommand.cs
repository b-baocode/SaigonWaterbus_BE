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
    private readonly ICustomBookingConfirmationEmailSender _confirmationEmailSender;

    public SyncCustomBookingPayOsPaymentCommandHandler(
        IApplicationDbContext context,
        IUserContext userContext,
        TimeProvider timeProvider,
        ICustomBookingPaymentGateway paymentGateway,
        ICustomBookingQuoteEmailSender quoteEmailSender,
        ICustomBookingConfirmationEmailSender confirmationEmailSender)
    {
        _context = context;
        _userContext = userContext;
        _timeProvider = timeProvider;
        _paymentGateway = paymentGateway;
        _quoteEmailSender = quoteEmailSender;
        _confirmationEmailSender = confirmationEmailSender;
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
            StorePaymentLinkIfAvailable(quote, paymentKind, payment);
            MarkTerminalPaymentStatusIfNeeded(quote, paymentKind, payment.Status);
            await _context.SaveChangesAsync(cancellationToken);
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

        if (!wasPaid)
        {
            await SendBoardingPassIfReadyAsync(customRequest.Id, cancellationToken);
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

    private async Task SendBoardingPassIfReadyAsync(
        Guid requestId,
        CancellationToken cancellationToken)
    {
        var customRequest = await CustomBookingRequestSupport.IncludeDetails(_context.Set<CustomBookingRequest>())
            .Include(x => x.Tickets)
            .SingleAsync(x => x.Id == requestId, cancellationToken);
        if (customRequest.Status != CustomBookingRequestStatus.Confirmed
            || customRequest.PassengerManifestStatus != PassengerManifestStatus.Completed
            || !CustomBookingPaymentSupport.IsFullyPaid(customRequest.Quote)
            || customRequest.Tickets.Any(x => x.Status == CustomBookingTicketStatus.Active))
        {
            return;
        }

        await CustomBookingTicketSupport.EnsureActiveTicketAsync(
            _context,
            customRequest,
            _timeProvider.GetUtcNow(),
            cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
        await _confirmationEmailSender.SendConfirmationAsync(customRequest, cancellationToken);
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

    private static void StorePaymentLinkIfAvailable(
        CustomBookingQuote quote,
        CustomBookingPaymentKind paymentKind,
        CustomBookingPaymentStatusResult payment)
    {
        if (paymentKind == CustomBookingPaymentKind.Deposit)
        {
            quote.DepositPaymentLinkId = string.IsNullOrWhiteSpace(payment.PaymentLinkId)
                ? quote.DepositPaymentLinkId
                : payment.PaymentLinkId;
            quote.DepositPaymentCheckoutUrl = string.IsNullOrWhiteSpace(payment.CheckoutUrl)
                ? quote.DepositPaymentCheckoutUrl
                : payment.CheckoutUrl;
            return;
        }

        quote.RemainingPaymentLinkId = string.IsNullOrWhiteSpace(payment.PaymentLinkId)
            ? quote.RemainingPaymentLinkId
            : payment.PaymentLinkId;
        quote.RemainingPaymentCheckoutUrl = string.IsNullOrWhiteSpace(payment.CheckoutUrl)
            ? quote.RemainingPaymentCheckoutUrl
            : payment.CheckoutUrl;
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

    private void MarkTerminalPaymentStatusIfNeeded(
        CustomBookingQuote quote,
        CustomBookingPaymentKind paymentKind,
        string status)
    {
        if (string.Equals(status, "CANCELLED", StringComparison.OrdinalIgnoreCase))
        {
            MarkPaymentCancelled(quote, paymentKind);
            return;
        }

        if (string.Equals(status, "EXPIRED", StringComparison.OrdinalIgnoreCase))
        {
            MarkPaymentExpired(quote, paymentKind);
        }
    }

    private void MarkPaymentCancelled(
        CustomBookingQuote quote,
        CustomBookingPaymentKind paymentKind)
    {
        var now = _timeProvider.GetUtcNow();
        if (paymentKind == CustomBookingPaymentKind.Deposit)
        {
            quote.DepositPaymentStatus = CustomBookingDepositPaymentStatus.Cancelled;
            quote.DepositPaymentCancelledAt ??= now;
            quote.DepositPaymentFailureReason = null;
            return;
        }

        quote.RemainingPaymentStatus = CustomBookingDepositPaymentStatus.Cancelled;
        quote.RemainingPaymentCancelledAt ??= now;
        quote.RemainingPaymentFailureReason = null;
    }

    private static void MarkPaymentExpired(
        CustomBookingQuote quote,
        CustomBookingPaymentKind paymentKind)
    {
        if (paymentKind == CustomBookingPaymentKind.Deposit)
        {
            quote.DepositPaymentStatus = CustomBookingDepositPaymentStatus.Expired;
            quote.DepositPaymentFailureReason = null;
            return;
        }

        quote.RemainingPaymentStatus = CustomBookingDepositPaymentStatus.Expired;
        quote.RemainingPaymentFailureReason = null;
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
