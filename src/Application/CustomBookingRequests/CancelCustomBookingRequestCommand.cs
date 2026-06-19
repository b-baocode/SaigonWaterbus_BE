using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Exceptions;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;

namespace SaigonWaterbus.Application.CustomBookingRequests;

public sealed record CancelCustomBookingRequestCommand(
    Guid Id,
    string Reason,
    string? RefundBankBin = null,
    string? RefundAccountNumber = null,
    string? RefundAccountName = null) : IRequest<CustomBookingRequestDto>;

public sealed class CancelCustomBookingRequestCommandValidator
    : AbstractValidator<CancelCustomBookingRequestCommand>
{
    public CancelCustomBookingRequestCommandValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty()
            .WithMessage("Id yêu cầu thuê tàu không hợp lệ.");
        RuleFor(x => x.Reason)
            .NotEmpty()
            .WithMessage("Lý do hủy là bắt buộc.")
            .MaximumLength(500)
            .WithMessage("Lý do hủy không được vượt quá 500 ký tự.");
        RuleFor(x => x.RefundBankBin)
            .MaximumLength(20)
            .WithMessage("Mã BIN ngân hàng nhận hoàn tiền không được vượt quá 20 ký tự.")
            .When(x => x.RefundBankBin is not null);
        RuleFor(x => x.RefundAccountNumber)
            .MaximumLength(50)
            .WithMessage("Số tài khoản nhận hoàn tiền không được vượt quá 50 ký tự.")
            .When(x => x.RefundAccountNumber is not null);
        RuleFor(x => x.RefundAccountName)
            .MaximumLength(150)
            .WithMessage("Tên tài khoản nhận hoàn tiền không được vượt quá 150 ký tự.")
            .When(x => x.RefundAccountName is not null);
    }
}

public sealed class CancelCustomBookingRequestCommandHandler
    : IRequestHandler<CancelCustomBookingRequestCommand, CustomBookingRequestDto>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;
    private readonly TimeProvider _timeProvider;
    private readonly ICustomBookingPaymentGateway _paymentGateway;

    public CancelCustomBookingRequestCommandHandler(
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
        CancelCustomBookingRequestCommand request,
        CancellationToken cancellationToken)
    {
        var actor = await AuthSupport.GetCurrentUserWithRoleAsync(_context, _userContext, cancellationToken);
        var canManage = AuthSupport.IsAdmin(actor);
        var isOwner = AuthSupport.IsCustomer(actor) && actor.Id == await _context.Set<CustomBookingRequest>()
            .Where(x => x.Id == request.Id)
            .Select(x => x.UserId)
            .SingleOrDefaultAsync(cancellationToken);

        if (!canManage && !isOwner)
        {
            throw new ForbiddenAccessException();
        }

        var customRequest = await CustomBookingRequestSupport.IncludeDetails(_context.Set<CustomBookingRequest>())
            .SingleOrDefaultAsync(x => x.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy yêu cầu thuê tàu.");

        CustomBookingRequestSupport.EnsureCanCancel(customRequest);

        var now = _timeProvider.GetUtcNow();
        var quote = customRequest.Quote;
        var paidAmount = CalculatePaidAmount(quote);
        var refund = paidAmount > 0
            ? CustomBookingRefundPolicy.Calculate(customRequest, paidAmount, now)
            : null;
        var refundBankBin = refund?.Amount > 0
            ? NormalizeRequiredRefundField(request.RefundBankBin, nameof(request.RefundBankBin))
            : null;
        var refundAccountNumber = refund?.Amount > 0
            ? NormalizeRequiredRefundField(request.RefundAccountNumber, nameof(request.RefundAccountNumber))
            : null;
        var refundAccountName = refund?.Amount > 0
            ? NormalizeRequiredRefundField(request.RefundAccountName, nameof(request.RefundAccountName))
            : null;

        await CancelPendingPaymentLinksAsync(quote, request.Reason, cancellationToken);

        if (quote?.DepositPaymentStatus == CustomBookingDepositPaymentStatus.Pending)
        {
            quote.DepositPaymentStatus = CustomBookingDepositPaymentStatus.Cancelled;
            quote.DepositPaymentCancelledAt = now;
        }

        if (quote?.RemainingPaymentStatus == CustomBookingDepositPaymentStatus.Pending)
        {
            quote.RemainingPaymentStatus = CustomBookingDepositPaymentStatus.Cancelled;
            quote.RemainingPaymentCancelledAt = now;
        }

        customRequest.Status = CustomBookingRequestStatus.Cancelled;
        customRequest.StatusReason = request.Reason.Trim();
        customRequest.CancelledAt = now;
        customRequest.CancelledByUserId = actor.Id;
        if (paidAmount > 0)
        {
            await RestorePromotionUsageAsync(quote, cancellationToken);
        }

        await _context.SaveChangesAsync(cancellationToken);

        if (paidAmount > 0 && quote is not null)
        {
            await ProcessRefundIfEligibleAsync(
                customRequest,
                quote,
                refund!,
                refundBankBin!,
                refundAccountNumber!,
                refundAccountName!,
                now,
                cancellationToken);
        }

        var routeSegments = await CustomBookingRequestSupport.GetMatchingRouteSegmentsAsync(
            _context,
            customRequest,
            cancellationToken);

        return CustomBookingRequestDto.From(customRequest, routeSegments);
    }

    private async Task CancelPendingPaymentLinksAsync(
        CustomBookingQuote? quote,
        string reason,
        CancellationToken cancellationToken)
    {
        if (quote is null)
        {
            return;
        }

        if (quote.DepositPaymentStatus == CustomBookingDepositPaymentStatus.Pending
            && quote.DepositPaymentOrderCode.HasValue)
        {
            await CancelPaymentLinkAsync(quote.DepositPaymentOrderCode.Value, reason, cancellationToken);
        }

        if (quote.RemainingPaymentStatus == CustomBookingDepositPaymentStatus.Pending
            && quote.RemainingPaymentOrderCode.HasValue)
        {
            await CancelPaymentLinkAsync(quote.RemainingPaymentOrderCode.Value, reason, cancellationToken);
        }
    }

    private async Task CancelPaymentLinkAsync(
        long orderCode,
        string reason,
        CancellationToken cancellationToken)
    {
        try
        {
            await _paymentGateway.CancelPaymentAsync(orderCode, reason.Trim(), cancellationToken);
        }
        catch (PaymentGatewayException ex)
        {
            throw AuthSupport.CreateValidationException(
                "payment",
                $"Không hủy được link thanh toán PayOS đang chờ xử lý: {ex.Message}");
        }
    }

    private async Task RestorePromotionUsageAsync(
        CustomBookingQuote? quote,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(quote?.DiscountCode))
        {
            return;
        }

        var promotion = await _context.Set<Promotion>()
            .SingleOrDefaultAsync(x => x.PromotionCode == quote.DiscountCode, cancellationToken);
        if (promotion is not null)
        {
            promotion.UsageCount = Math.Max(0, promotion.UsageCount - 1);
        }
    }

    private static decimal CalculatePaidAmount(CustomBookingQuote? quote)
    {
        if (quote is null)
        {
            return 0m;
        }

        var paidAmount = quote.DepositPaymentStatus == CustomBookingDepositPaymentStatus.Paid
            ? quote.DepositAmount
            : 0m;

        if (quote.RemainingPaymentStatus == CustomBookingDepositPaymentStatus.Paid)
        {
            paidAmount += quote.RemainingAmount;
        }

        return paidAmount;
    }

    private async Task ProcessRefundIfEligibleAsync(
        CustomBookingRequest customRequest,
        CustomBookingQuote quote,
        CustomBookingRefundQuote refund,
        string? bankBin,
        string? accountNumber,
        string? accountName,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(quote.RefundReferenceId))
        {
            return;
        }

        quote.RefundEligiblePercent = refund.Percent;
        quote.RefundAmount = refund.Amount;
        quote.RefundPolicyNote = refund.Note;

        if (refund.Amount <= 0)
        {
            quote.RefundStatus = "NotEligible";
            quote.RefundProcessedAt = now;
            await _context.SaveChangesAsync(cancellationToken);
            return;
        }

        var referenceId = CreateRefundReferenceId(customRequest);
        quote.RefundBankBin = bankBin!;
        quote.RefundAccountNumber = accountNumber!;
        quote.RefundAccountName = accountName!;
        quote.RefundReferenceId = referenceId;
        quote.RefundStatus = "Pending";
        quote.RefundRequestedAt = now;
        await _context.SaveChangesAsync(cancellationToken);

        try
        {
            var result = await _paymentGateway.CreateRefundPayoutAsync(
                new CustomBookingRefundPayoutRequest(
                    referenceId,
                    ToPayOsAmount(refund.Amount),
                    CreateRefundDescription(customRequest),
                    bankBin!,
                    accountNumber!,
                    accountName!,
                    Guid.NewGuid().ToString("D")),
                cancellationToken);

            quote.RefundPayoutId = result.PayoutId;
            quote.RefundStatus = result.Status;
            quote.RefundFailureReason = null;
            quote.RefundProcessedAt = _timeProvider.GetUtcNow();
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (PaymentGatewayException ex)
        {
            quote.RefundStatus = "Failed";
            quote.RefundFailureReason = ex.Message;
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    private static string NormalizeRequiredRefundField(string? value, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw AuthSupport.CreateValidationException(
                propertyName,
                "Thông tin tài khoản nhận hoàn tiền là bắt buộc khi booking đủ điều kiện hoàn tiền.");
        }

        return value.Trim();
    }

    private static long ToPayOsAmount(decimal amount)
    {
        if (amount <= 0 || decimal.Truncate(amount) != amount || amount > long.MaxValue)
        {
            throw AuthSupport.CreateValidationException("refundAmount", "Số tiền hoàn phải là số nguyên VND lớn hơn 0.");
        }

        return (long)amount;
    }

    private static string CreateRefundReferenceId(CustomBookingRequest request) =>
        $"CBR-{request.Id:N}"[..36].ToUpperInvariant();

    private static string CreateRefundDescription(CustomBookingRequest request) =>
        $"Hoan tien SWB {request.Id.ToString("N")[..8].ToUpperInvariant()}";
}
