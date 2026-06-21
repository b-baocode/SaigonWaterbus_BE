using System.Globalization;
using System.Text;
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
            .Must(CustomBookingRefundAccountValidation.IsValidBankBin)
            .WithMessage("Mã BIN ngân hàng nhận hoàn tiền phải gồm đúng 6 chữ số theo chuẩn PayOS.")
            .When(x => !string.IsNullOrWhiteSpace(x.RefundBankBin));
        RuleFor(x => x.RefundAccountNumber)
            .Must(CustomBookingRefundAccountValidation.IsValidAccountNumber)
            .WithMessage("Số tài khoản nhận hoàn tiền chỉ được gồm chữ số và không vượt quá 50 ký tự.")
            .When(x => !string.IsNullOrWhiteSpace(x.RefundAccountNumber));
        RuleFor(x => x.RefundAccountName)
            .MaximumLength(CustomBookingRefundAccountValidation.MaxAccountNameLength)
            .WithMessage("Tên tài khoản nhận hoàn tiền không được vượt quá 150 ký tự.")
            .When(x => !string.IsNullOrWhiteSpace(x.RefundAccountName));
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
            ? CustomBookingRefundAccountValidation.NormalizeRequiredBankBin(
                request.RefundBankBin,
                nameof(request.RefundBankBin))
            : null;
        var refundAccountNumber = refund?.Amount > 0
            ? CustomBookingRefundAccountValidation.NormalizeRequiredAccountNumber(
                request.RefundAccountNumber,
                nameof(request.RefundAccountNumber))
            : null;
        var refundAccountName = refund?.Amount > 0
            ? CustomBookingRefundAccountValidation.NormalizeRequiredAccountName(
                request.RefundAccountName,
                nameof(request.RefundAccountName))
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

        customRequest.Status = CustomBookingRequestStatus.Cancelled;
        customRequest.StatusReason = request.Reason.Trim();
        customRequest.CancelledAt = now;
        customRequest.CancelledByUserId = actor.Id;
        await CustomBookingVesselReservations.ReleaseAsync(
            _context,
            customRequest.Id,
            VesselReservationStatus.Cancelled,
            now,
            request.Reason.Trim(),
            cancellationToken);

        if (paidAmount > 0)
        {
            await RestorePromotionUsageAsync(quote, cancellationToken);
        }

        await _context.SaveChangesAsync(cancellationToken);

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
            await _paymentGateway.CancelPaymentAsync(
                orderCode,
                CreatePayOsCancellationReason(reason),
                cancellationToken);
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

        if (!string.IsNullOrWhiteSpace(quote.RefundReferenceId))
        {
            if (!CustomBookingRefundPayoutStatus.IsAccepted(quote))
            {
                throw AuthSupport.CreateValidationException(
                    "refund",
                    "Lệnh hoàn tiền PayOS trước đó chưa thành công. Vui lòng đối soát hoặc retry hoàn tiền trước khi hủy booking.");
            }

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
            if (!CustomBookingRefundPayoutStatus.IsAccepted(quote))
            {
                quote.RefundFailureReason = CustomBookingRefundPayoutStatus.CreateNotAcceptedReason(
                    result.Status,
                    result.Description);
                await _context.SaveChangesAsync(cancellationToken);
                throw AuthSupport.CreateValidationException("refund", quote.RefundFailureReason);
            }
        }
        catch (PaymentGatewayException ex)
        {
            quote.RefundStatus = "Failed";
            quote.RefundFailureReason = ex.Message;
            await _context.SaveChangesAsync(cancellationToken);
            throw AuthSupport.CreateValidationException("refund", ex.Message);
        }
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

    private static string CreatePayOsCancellationReason(string reason)
    {
        var normalized = RemoveVietnameseDiacritics(reason.Trim());
        var ascii = new string(normalized
            .Select(static c => c is >= ' ' and <= '~' ? c : ' ')
            .ToArray());
        ascii = string.Join(' ', ascii.Split(' ', StringSplitOptions.RemoveEmptyEntries));

        if (string.IsNullOrWhiteSpace(ascii))
        {
            return "Huy booking custom";
        }

        const int maxPayOsReasonLength = 100;
        return ascii.Length <= maxPayOsReasonLength
            ? ascii
            : ascii[..maxPayOsReasonLength].TrimEnd();
    }

    private static string RemoveVietnameseDiacritics(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var c in value.Normalize(NormalizationForm.FormD))
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(c);
            if (category == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            builder.Append(c switch
            {
                'đ' => 'd',
                'Đ' => 'D',
                _ => c
            });
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }
}
