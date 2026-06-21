using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;

namespace SaigonWaterbus.Application.CustomBookingRequests;

public sealed record QuoteCustomBookingRequestCommand(
    Guid Id,
    decimal DepositPercent,
    string? PriceNote,
    decimal ServiceFeeAmount = 0m,
    string? DiscountCode = null) : IRequest<CustomBookingRequestDto>;

public sealed class QuoteCustomBookingRequestCommandValidator : AbstractValidator<QuoteCustomBookingRequestCommand>
{
    public QuoteCustomBookingRequestCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.DepositPercent)
            .GreaterThan(0)
            .LessThanOrEqualTo(100)
            .PrecisionScale(5, 2, false)
            .WithMessage("Phần trăm đặt cọc phải lớn hơn 0 và không vượt quá 100.");
        RuleFor(x => x.PriceNote)
            .MaximumLength(1000)
            .WithMessage("Ghi chú báo giá không được vượt quá 1000 ký tự.")
            .When(x => x.PriceNote is not null);
        RuleFor(x => x.ServiceFeeAmount)
            .GreaterThanOrEqualTo(0)
            .WithMessage("Phụ phí dịch vụ không được âm.")
            .PrecisionScale(12, 2, false)
            .WithMessage("Phụ phí dịch vụ không được vượt quá 12 chữ số và tối đa 2 số thập phân.")
            .Must(x => decimal.Truncate(x) == x)
            .WithMessage("Phụ phí dịch vụ phải là số nguyên VND.");
        RuleFor(x => x.DiscountCode)
            .MaximumLength(50)
            .WithMessage("Mã giảm giá không được vượt quá 50 ký tự.")
            .When(x => x.DiscountCode is not null);
    }
}

public sealed class QuoteCustomBookingRequestCommandHandler
    : IRequestHandler<QuoteCustomBookingRequestCommand, CustomBookingRequestDto>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;
    private readonly TimeProvider _timeProvider;
    private readonly ICustomBookingQuoteEmailSender _quoteEmailSender;
    private readonly IDatabaseExceptionClassifier _databaseExceptionClassifier;

    public QuoteCustomBookingRequestCommandHandler(
        IApplicationDbContext context,
        IUserContext userContext,
        TimeProvider timeProvider,
        ICustomBookingQuoteEmailSender quoteEmailSender,
        IDatabaseExceptionClassifier? databaseExceptionClassifier = null)
    {
        _context = context;
        _userContext = userContext;
        _timeProvider = timeProvider;
        _quoteEmailSender = quoteEmailSender;
        _databaseExceptionClassifier = databaseExceptionClassifier ?? NoOpDatabaseExceptionClassifier.Instance;
    }

    public async Task<CustomBookingRequestDto> Handle(
        QuoteCustomBookingRequestCommand request,
        CancellationToken cancellationToken)
    {
        var actor = await CustomBookingRequestSupport.EnsureCurrentUserCanManageCustomBookingRequestsAsync(
            _context,
            _userContext,
            cancellationToken);

        var customRequest = await CustomBookingRequestSupport.IncludeDetails(_context.Set<CustomBookingRequest>())
            .SingleOrDefaultAsync(x => x.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy yêu cầu thuê tàu.");

        CustomBookingRequestSupport.EnsureCanQuote(customRequest);
        CustomBookingRequestSupport.EnsureVesselMatchesRequest(customRequest, customRequest.AssignedVessel!);
        var now = _timeProvider.GetUtcNow();
        var validUntil = CustomBookingRequestSupport.CalculateQuoteValidUntil(customRequest, now);
        var routeSegments = await CustomBookingRequestSupport.GetMatchingRouteSegmentsAsync(
            _context,
            customRequest,
            cancellationToken);
        CustomBookingRequestSupport.ApplyRouteEstimate(customRequest, routeSegments);
        if (await CustomBookingVesselReservations.ExpireStaleReservationsAsync(
            _context,
            now,
            cancellationToken) > 0)
        {
            await _context.SaveChangesAsync(cancellationToken);
        }

        await CustomBookingVesselReservations.EnsureVesselAvailableAsync(
            _context,
            customRequest,
            customRequest.AssignedVesselId!.Value,
            now,
            cancellationToken);
        var rentalPrice = CustomBookingRequestSupport.GetRequiredRentalPriceOrThrow(
            customRequest,
            customRequest.AssignedVessel!);
        var baseVesselPrice = CustomBookingRequestSupport.CalculateRentalPrice(customRequest, rentalPrice);
        var grossPrice = decimal.Round(
            baseVesselPrice + request.ServiceFeeAmount,
            0,
            MidpointRounding.AwayFromZero);
        var discount = await ResolveDiscountAsync(request.DiscountCode, grossPrice, now, cancellationToken);
        var quotedPrice = grossPrice - discount.Amount;
        if (quotedPrice <= 0)
        {
            throw AuthSupport.CreateValidationException(
                nameof(request.DiscountCode),
                "Mã giảm giá không được làm tổng tiền sau giảm nhỏ hơn hoặc bằng 0.");
        }

        var depositAmount = decimal.Round(
            quotedPrice * request.DepositPercent / 100m,
            0,
            MidpointRounding.AwayFromZero);
        var remainingAmount = quotedPrice - depositAmount;
        if (depositAmount <= 0)
        {
            throw AuthSupport.CreateValidationException(
                nameof(request.DepositPercent),
                "Tiền đặt cọc sau giảm giá phải lớn hơn 0 VND.");
        }

        if (remainingAmount > 0m)
        {
            var remainingPaymentDeadline = CustomBookingPaymentSupport.ResolveRemainingPaymentDeadline(customRequest);
            if (now >= remainingPaymentDeadline)
            {
                throw AuthSupport.CreateValidationException(
                    nameof(request.DepositPercent),
                    "Booking trong vòng 24 giờ trước khởi hành phải thanh toán 100% ngay khi chấp nhận báo giá.");
            }

            if (validUntil > remainingPaymentDeadline)
            {
                validUntil = remainingPaymentDeadline;
            }
        }

        if (customRequest.Quote is null)
        {
            customRequest.Quote = new CustomBookingQuote
            {
                CustomBookingRequestId = customRequest.Id
            };
            _context.Set<CustomBookingQuote>().Add(customRequest.Quote);
        }

        customRequest.Quote.QuotedPrice = quotedPrice;
        customRequest.Quote.ServiceFeeAmount = request.ServiceFeeAmount;
        customRequest.Quote.DiscountCode = discount.Code;
        customRequest.Quote.DiscountAmount = discount.Amount;
        customRequest.Quote.DepositPercent = request.DepositPercent;
        customRequest.Quote.DepositAmount = depositAmount;
        customRequest.Quote.RemainingAmount = remainingAmount;
        customRequest.Quote.Currency = CustomBookingRequestSupport.NormalizeCurrency(rentalPrice.Currency);
        customRequest.Quote.PriceNote = string.IsNullOrWhiteSpace(request.PriceNote)
            ? rentalPrice.Note
            : request.PriceNote.Trim();
        customRequest.Quote.ValidUntil = validUntil;
        ResetPaymentAndRefundState(customRequest.Quote);
        customRequest.Status = CustomBookingRequestStatus.Quoted;
        customRequest.StatusReason = null;
        customRequest.QuotedAt = now;
        customRequest.QuotedByUserId = actor.Id;
        customRequest.QuoteAcceptedAt = null;
        await CustomBookingVesselReservations.HoldUntilQuoteExpiryAsync(
            _context,
            customRequest,
            actor.Id,
            now,
            cancellationToken);

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (_databaseExceptionClassifier.IsExclusionConstraintViolation(ex))
        {
            throw CustomBookingVesselReservations.CreateUnavailableException();
        }

        customRequest = await CustomBookingRequestSupport.IncludeDetails(_context.Set<CustomBookingRequest>())
            .SingleAsync(x => x.Id == customRequest.Id, cancellationToken);

        await _quoteEmailSender.SendQuoteAsync(customRequest, cancellationToken);

        return CustomBookingRequestDto.From(customRequest, routeSegments);
    }

    private async Task<(string? Code, decimal Amount)> ResolveDiscountAsync(
        string? discountCode,
        decimal subtotalAmount,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(discountCode))
        {
            return (null, 0m);
        }

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

    private static void ResetPaymentAndRefundState(CustomBookingQuote quote)
    {
        quote.DepositPaymentStatus = CustomBookingDepositPaymentStatus.NotCreated;
        quote.DepositPaymentOrderCode = null;
        quote.DepositPaymentLinkId = null;
        quote.DepositPaymentCheckoutUrl = null;
        quote.DepositPaymentQrCode = null;
        quote.DepositPaymentCreatedAt = null;
        quote.DepositPaymentPaidAt = null;
        quote.DepositPaymentCancelledAt = null;
        quote.DepositPaymentFailureReason = null;

        quote.RemainingPaymentStatus = CustomBookingDepositPaymentStatus.NotCreated;
        quote.RemainingPaymentOrderCode = null;
        quote.RemainingPaymentLinkId = null;
        quote.RemainingPaymentCheckoutUrl = null;
        quote.RemainingPaymentQrCode = null;
        quote.RemainingPaymentCreatedAt = null;
        quote.RemainingPaymentPaidAt = null;
        quote.RemainingPaymentCancelledAt = null;
        quote.RemainingPaymentFailureReason = null;

        quote.RefundEligiblePercent = 0m;
        quote.RefundAmount = 0m;
        quote.RefundPolicyNote = null;
        quote.RefundBankBin = null;
        quote.RefundAccountNumber = null;
        quote.RefundAccountName = null;
        quote.RefundReferenceId = null;
        quote.RefundPayoutId = null;
        quote.RefundStatus = null;
        quote.RefundFailureReason = null;
        quote.RefundRequestedAt = null;
        quote.RefundProcessedAt = null;
    }
}
