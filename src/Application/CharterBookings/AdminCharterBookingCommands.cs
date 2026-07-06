using FluentValidation.Results;
using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Application.Promotions;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.CharterBookings;

public sealed record GetAdminCharterBookingListQuery : IRequest<IReadOnlyList<AdminCharterBookingListItemDto>>;

public sealed class GetAdminCharterBookingListQueryHandler
    : IRequestHandler<GetAdminCharterBookingListQuery, IReadOnlyList<AdminCharterBookingListItemDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;

    public GetAdminCharterBookingListQueryHandler(IApplicationDbContext context, IUserContext userContext)
    {
        _context = context;
        _userContext = userContext;
    }

    public async Task<IReadOnlyList<AdminCharterBookingListItemDto>> Handle(
        GetAdminCharterBookingListQuery request,
        CancellationToken cancellationToken)
    {
        await AuthSupport.EnsureCurrentUserIsAdminAsync(_context, _userContext, cancellationToken);

        return await CharterBookingQuerySupport.BuildBaseQuery(_context)
            .OrderByDescending(x => x.Created)
            .Select(x => new AdminCharterBookingListItemDto(
                x.Id,
                x.BookingCode,
                x.Created,
                x.DepartureDate.GetValueOrDefault(),
                x.BookingStatus.ToString(),
                x.PaymentStatus,
                x.PassengerCount.GetValueOrDefault(),
                x.AdultCount.GetValueOrDefault(),
                x.ChildCount.GetValueOrDefault(),
                x.TotalAmount,
                x.ContactName,
                x.ContactPhone,
                x.ContactEmail,
                x.Boat != null ? x.Boat.Name : null))
            .ToListAsync(cancellationToken);
    }
}

public sealed record GetAdminCharterBookingDetailQuery(Guid BookingId) : IRequest<CharterBookingDetailDto>;

public sealed class GetAdminCharterBookingDetailQueryHandler
    : IRequestHandler<GetAdminCharterBookingDetailQuery, CharterBookingDetailDto>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;

    public GetAdminCharterBookingDetailQueryHandler(IApplicationDbContext context, IUserContext userContext)
    {
        _context = context;
        _userContext = userContext;
    }

    public async Task<CharterBookingDetailDto> Handle(
        GetAdminCharterBookingDetailQuery request,
        CancellationToken cancellationToken)
    {
        await AuthSupport.EnsureCurrentUserIsAdminAsync(_context, _userContext, cancellationToken);

        var booking = await CharterBookingQuerySupport.BuildDetailQuery(_context)
            .SingleOrDefaultAsync(x => x.Id == request.BookingId, cancellationToken)
            ?? throw new NotFoundException("Charter booking not found.");

        var relatedRoutes = await CharterBookingRoutePricingSupport.LoadRelatedRoutesAsync(
            _context,
            booking,
            cancellationToken);

        return CharterBookingQuerySupport.ToDetailDto(booking, relatedRoutes);
    }
}

public sealed record UpdateCharterBookingStatusCommand(
    Guid BookingId,
    BookingStatus BookingStatus) : IRequest<CharterBookingDetailDto>;

public sealed class UpdateCharterBookingStatusCommandValidator
    : AbstractValidator<UpdateCharterBookingStatusCommand>
{
    public UpdateCharterBookingStatusCommandValidator()
    {
        RuleFor(x => x.BookingId).NotEmpty();
        RuleFor(x => x.BookingStatus).IsInEnum();
    }
}

public sealed class UpdateCharterBookingStatusCommandHandler
    : IRequestHandler<UpdateCharterBookingStatusCommand, CharterBookingDetailDto>
{
    private const string PendingPaymentStatus = "Pending";
    private const string PaidPaymentStatus = "Paid";
    private const string DepositPaidBookingPaymentStatus = "DepositPaid";
    private const string RefundedBookingPaymentStatus = "Refunded";

    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;
    private readonly IBoatHoldService _boatHoldService;

    public UpdateCharterBookingStatusCommandHandler(
        IApplicationDbContext context,
        IUserContext userContext,
        IBoatHoldService? boatHoldService = null)
    {
        _context = context;
        _userContext = userContext;
        _boatHoldService = boatHoldService ?? NullBoatHoldService.Instance;
    }

    public async Task<CharterBookingDetailDto> Handle(
        UpdateCharterBookingStatusCommand request,
        CancellationToken cancellationToken)
    {
        await AuthSupport.EnsureCurrentUserIsAdminAsync(_context, _userContext, cancellationToken);

        var booking = await CharterBookingQuerySupport.BuildDetailQuery(_context)
            .SingleOrDefaultAsync(x => x.Id == request.BookingId, cancellationToken)
            ?? throw new NotFoundException("Charter booking not found.");
        var previousStatus = booking.BookingStatus;
        var previousBoatId = booking.BoatId;
        var previousDepartureDate = booking.DepartureDate;
        var previousStartTime = booking.StartTime;
        var previousRentalUnit = booking.RentalUnit;
        var previousDurationValue = booking.DurationValue.GetValueOrDefault();

        ApplyStatusUpdate(booking, request.BookingStatus);

        if (!PromotionUsageSupport.ReleasesPromotionUsage(previousStatus)
            && PromotionUsageSupport.ReleasesPromotionUsage(request.BookingStatus))
        {
            PromotionUsageSupport.DecrementUsage(booking.Promotion);
        }

        await _context.SaveChangesAsync(cancellationToken);
        if (request.BookingStatus is BookingStatus.PendingQuote or BookingStatus.Cancelled or BookingStatus.Expired)
        {
            await _boatHoldService.ReleaseAsync(
                booking.Id,
                previousBoatId,
                previousDepartureDate.GetValueOrDefault(),
                previousStartTime,
                previousRentalUnit.GetValueOrDefault(),
                previousDurationValue,
                cancellationToken);
        }

        var relatedRoutes = await CharterBookingRoutePricingSupport.LoadRelatedRoutesAsync(
            _context,
            booking,
            cancellationToken);

        return CharterBookingQuerySupport.ToDetailDto(booking, relatedRoutes);
    }

    private static void ApplyStatusUpdate(Booking booking, BookingStatus targetStatus)
    {
        if (booking.BookingStatus == targetStatus)
        {
            return;
        }

        switch (targetStatus)
        {
            case BookingStatus.PendingPayment:
                throw CreateStatusValidation(targetStatus,
                    "Charter booking không dùng trạng thái PendingPayment. Dùng PendingQuote khi chưa được admin chốt giá.");

            case BookingStatus.PendingQuote:
                EnsureNoPendingOrPaidPayments(booking, targetStatus);
                booking.HoldExpiresAt = null;
                break;

            case BookingStatus.Quoted:
                EnsureCanMarkQuoted(booking, targetStatus);
                break;

            case BookingStatus.Confirmed:
                EnsureCanMarkConfirmed(booking, targetStatus);
                break;

            case BookingStatus.Cancelled:
                EnsureCanCancel(booking, targetStatus);
                booking.HoldExpiresAt = null;
                UpdateTickets(booking, TicketStatus.Cancelled);
                break;

            case BookingStatus.Expired:
                EnsureCanExpire(booking, targetStatus);
                booking.HoldExpiresAt = null;
                UpdateTickets(booking, TicketStatus.Expired);
                break;

            case BookingStatus.Completed:
                EnsureCanComplete(booking, targetStatus);
                break;

            case BookingStatus.Refunded:
                EnsureCanMarkRefunded(booking, targetStatus);
                break;

            default:
                throw CreateStatusValidation(targetStatus, "Trạng thái charter booking không hợp lệ.");
        }

        booking.BookingStatus = targetStatus;
    }

    private static void EnsureCanMarkQuoted(Booking booking, BookingStatus targetStatus)
    {
        EnsureNoPendingOrPaidPayments(booking, targetStatus);

        if (!booking.BoatId.HasValue || booking.TotalAmount <= 0)
        {
            throw CreateStatusValidation(targetStatus,
                "Không thể chuyển sang Quoted khi booking chưa có tàu hoặc chưa có giá. Hãy dùng API quote để chọn tàu và chốt giá.");
        }
    }

    private static void EnsureCanMarkConfirmed(Booking booking, BookingStatus targetStatus)
    {
        if (IsBookingPaymentStatus(booking, DepositPaidBookingPaymentStatus, PaidPaymentStatus)
            || booking.Payments.Any(x => IsPaymentStatus(x.PaymentStatus, PaidPaymentStatus)))
        {
            return;
        }

        throw CreateStatusValidation(targetStatus,
            "Không thể chuyển sang Confirmed khi booking chưa có thanh toán đặt cọc hoặc thanh toán đủ.");
    }

    private static void EnsureCanCancel(Booking booking, BookingStatus targetStatus)
    {
        if (booking.BookingStatus is BookingStatus.Completed or BookingStatus.Refunded)
        {
            throw CreateStatusValidation(targetStatus,
                "Không thể hủy charter booking đã hoàn tất hoặc đã hoàn tiền.");
        }
    }

    private static void EnsureCanExpire(Booking booking, BookingStatus targetStatus)
    {
        if (booking.BookingStatus is BookingStatus.Confirmed or BookingStatus.Completed or BookingStatus.Refunded)
        {
            throw CreateStatusValidation(targetStatus,
                "Không thể chuyển charter booking đã xác nhận, đã hoàn tất hoặc đã hoàn tiền sang Expired.");
        }

        EnsureNoPendingOrPaidPayments(booking, targetStatus);
    }

    private static void EnsureCanComplete(Booking booking, BookingStatus targetStatus)
    {
        if (!IsBookingPaymentStatus(booking, PaidPaymentStatus))
        {
            throw CreateStatusValidation(targetStatus,
                "Chỉ có thể hoàn tất charter booking đã thanh toán đủ.");
        }
    }

    private static void EnsureCanMarkRefunded(Booking booking, BookingStatus targetStatus)
    {
        if (!IsBookingPaymentStatus(booking, RefundedBookingPaymentStatus))
        {
            throw CreateStatusValidation(targetStatus,
                "Không thể chuyển sang Refunded khi paymentStatus chưa phải Refunded. Hãy dùng luồng hoàn tiền trước.");
        }
    }

    private static void EnsureNoPendingOrPaidPayments(Booking booking, BookingStatus targetStatus)
    {
        if (booking.Payments.Any(x => IsPaymentStatus(x.PaymentStatus, PendingPaymentStatus, PaidPaymentStatus)))
        {
            throw CreateStatusValidation(targetStatus,
                "Không thể chuyển về trạng thái này khi booking đã có payment đang chờ hoặc đã thanh toán.");
        }
    }

    private static void UpdateTickets(Booking booking, TicketStatus ticketStatus)
    {
        foreach (var ticket in booking.Tickets)
        {
            ticket.TicketStatus = ticketStatus;
        }
    }

    private static bool IsBookingPaymentStatus(Booking booking, params string[] statuses) =>
        statuses.Any(status => string.Equals(booking.PaymentStatus, status, StringComparison.OrdinalIgnoreCase));

    private static bool IsPaymentStatus(string paymentStatus, params string[] statuses) =>
        statuses.Any(status => string.Equals(paymentStatus, status, StringComparison.OrdinalIgnoreCase));

    private static ValidationException CreateStatusValidation(BookingStatus targetStatus, string message) =>
        new([new ValidationFailure("bookingStatus", message)]);
}

public sealed record QuoteCharterBookingCommand(
    Guid BookingId,
    Guid BoatId,
    decimal? SubtotalAmount = null,
    BoatRentalUnit? RentalUnit = null,
    int? DurationValue = null,
    string? PromotionCode = null) : IRequest<QuoteCharterBookingResult>;

public sealed class QuoteCharterBookingCommandValidator : AbstractValidator<QuoteCharterBookingCommand>
{
    public QuoteCharterBookingCommandValidator()
    {
        RuleFor(x => x.BookingId).NotEmpty();
        RuleFor(x => x.BoatId).NotEmpty();
        RuleFor(x => x.SubtotalAmount)
            .GreaterThan(0)
            .When(x => x.SubtotalAmount.HasValue)
            .WithMessage("Giá chốt phải lớn hơn 0.");
        RuleFor(x => x.RentalUnit).IsInEnum().When(x => x.RentalUnit.HasValue);
        RuleFor(x => x.DurationValue)
            .GreaterThan(0)
            .LessThanOrEqualTo(60)
            .When(x => x.DurationValue.HasValue)
            .WithMessage("Thời lượng thuê phải từ 1 đến 60.");
        RuleFor(x => x.PromotionCode).MaximumLength(50).When(x => x.PromotionCode is not null);
    }
}

public sealed class QuoteCharterBookingCommandHandler
    : IRequestHandler<QuoteCharterBookingCommand, QuoteCharterBookingResult>
{
    private const string PendingPaymentStatus = "Pending";
    private const string PaidPaymentStatus = "Paid";
    private const string UnpaidBookingPaymentStatus = "Unpaid";

    private static readonly TimeSpan HoldDuration = TimeSpan.FromHours(2);

    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;
    private readonly IBoatHoldService _boatHoldService;
    private readonly TimeProvider _timeProvider;

    public QuoteCharterBookingCommandHandler(
        IApplicationDbContext context,
        IUserContext userContext,
        TimeProvider timeProvider,
        IBoatHoldService? boatHoldService = null)
    {
        _context = context;
        _userContext = userContext;
        _boatHoldService = boatHoldService ?? NullBoatHoldService.Instance;
        _timeProvider = timeProvider;
    }

    public async Task<QuoteCharterBookingResult> Handle(
        QuoteCharterBookingCommand request,
        CancellationToken cancellationToken)
    {
        await AuthSupport.EnsureCurrentUserIsAdminAsync(_context, _userContext, cancellationToken);

        var booking = await CharterBookingQuerySupport.BuildBaseQuery(_context)
            .Include(x => x.Payments)
            .Include(x => x.Promotion)
            .Include(x => x.FromStation)
            .Include(x => x.ToStation)
            .Include(x => x.ItineraryStops)
                .ThenInclude(x => x.Station)
            .SingleOrDefaultAsync(x => x.Id == request.BookingId, cancellationToken)
            ?? throw new NotFoundException("Charter booking not found.");

        EnsureCanQuote(booking);

        var boat = await _context.Set<Boat>()
            .SingleOrDefaultAsync(x => x.Id == request.BoatId, cancellationToken)
            ?? throw new NotFoundException("Boat not found.");

        if (boat.Status != BoatStatus.Active)
        {
            throw new ValidationException([new ValidationFailure(nameof(request.BoatId),
                "Tàu hiện không khả dụng để thuê.")]);
        }

        if (booking.PassengerCount.GetValueOrDefault() > boat.SeatCount)
        {
            throw new ValidationException([new ValidationFailure(nameof(booking.PassengerCount),
                $"Số khách vượt quá sức chứa của tàu ({boat.SeatCount}).")]);
        }

        var requestedBoatTypes = CharterBookingBoatSelectionSupport.FromStorageValue(booking.RequestedBoatTypes);
        if (requestedBoatTypes.Count == 0 && booking.PreferredSeatSetupType.HasValue)
        {
            requestedBoatTypes = [booking.PreferredSeatSetupType.Value];
        }

        if (requestedBoatTypes.Count > 0 && !requestedBoatTypes.Contains(boat.SeatSetupType))
        {
            throw new ValidationException([new ValidationFailure(nameof(request.BoatId),
                $"Tàu được chọn là {boat.SeatSetupType}, không trùng kiểu tàu khách yêu cầu.")]);
        }

        var now = _timeProvider.GetUtcNow();

        var conflicts = await CharterBookingQuerySupport.BuildBaseQuery(_context)
            .Where(x => x.Id != booking.Id
                && x.BoatId == boat.Id
                && x.DepartureDate == booking.DepartureDate
                && (x.BookingStatus == BookingStatus.Quoted || x.BookingStatus == BookingStatus.Confirmed))
            .ToListAsync(cancellationToken);

        foreach (var conflict in conflicts)
        {
            if (conflict.BookingStatus == BookingStatus.Confirmed
                || (conflict.HoldExpiresAt.HasValue && conflict.HoldExpiresAt.Value > now))
            {
                throw new ValidationException([new ValidationFailure(nameof(request.BoatId),
                    "Tàu đã được giữ cho một booking khác trong ngày này.")]);
            }

            conflict.BookingStatus = BookingStatus.Expired;
            conflict.HoldExpiresAt = null;
        }

        var previousBoatId = booking.BoatId;
        var previousDepartureDate = booking.DepartureDate;
        var previousStartTime = booking.StartTime;
        var previousRentalUnit = booking.RentalUnit;
        var previousDurationValue = booking.DurationValue.GetValueOrDefault();
        var wasAlreadyPriced = booking.TotalAmount > 0;
        var previousPromotionId = booking.PromotionId;
        var rentalUnit = request.RentalUnit ?? booking.RentalUnit.GetValueOrDefault();
        var requestedDurationValue = request.DurationValue ?? booking.DurationValue.GetValueOrDefault();
        var relatedRoutes = await CharterBookingRoutePricingSupport.LoadRelatedRoutesAsync(
            _context,
            booking,
            cancellationToken);
        var automaticPricing = CharterBookingRoutePricingSupport.EstimatePrice(
            booking,
            boat,
            rentalUnit,
            requestedDurationValue,
            relatedRoutes);
        if (!request.SubtotalAmount.HasValue)
        {
            CharterBookingRoutePricingSupport.EnsureCanAutoPrice(rentalUnit, automaticPricing.RouteEstimate);
        }

        var subtotal = request.SubtotalAmount ?? automaticPricing.SubtotalAmount;
        var chargeableDurationValue = automaticPricing.ChargeableDurationValue;
        var holdDurationValue = requestedDurationValue;
        var promotion = await ResolvePromotionForQuoteAsync(booking, request, subtotal, now, cancellationToken);
        if (promotion is not null)
        {
            if (!booking.UserId.HasValue)
            {
                throw new ValidationException([new ValidationFailure(
                    nameof(booking.UserId),
                    "Booking không có tài khoản để kiểm tra promotion.")]);
            }

            await PromotionUsageSupport.EnsureAccountCanUsePromotionAsync(
                _context,
                promotion,
                booking.UserId.Value,
                nameof(request.PromotionCode),
                cancellationToken,
                booking.Id);
        }
        var discount = CharterBookingPricingSupport.CalculateDiscount(promotion, subtotal);
        var total = subtotal - discount;
        var holdExpiresAt = now + HoldDuration;

        if (!await _boatHoldService.TryHoldAsync(
                booking.Id,
                boat.Id,
                booking.DepartureDate.GetValueOrDefault(),
                booking.StartTime,
                rentalUnit,
                holdDurationValue,
                holdExpiresAt,
                cancellationToken))
        {
            throw new ValidationException([new ValidationFailure(nameof(request.BoatId),
                "Tàu đang được giữ tạm thời cho booking khác. Vui lòng chọn tàu khác hoặc thử lại sau.")]);
        }

        booking.BoatId = boat.Id;
        booking.RentalUnit = rentalUnit;
        booking.DurationValue = requestedDurationValue;
        booking.PromotionId = promotion?.Id;
        booking.SubtotalAmount = subtotal;
        booking.DiscountAmount = discount;
        booking.TotalAmount = total;
        booking.DepositAmount = 0;
        booking.RemainingAmount = total;
        booking.PaymentStatus = UnpaidBookingPaymentStatus;
        booking.BookingStatus = BookingStatus.Quoted;
        booking.HoldExpiresAt = holdExpiresAt;

        if (wasAlreadyPriced && previousPromotionId.HasValue && previousPromotionId != promotion?.Id)
        {
            var previousPromotion = await _context.Set<Promotion>()
                .SingleOrDefaultAsync(x => x.Id == previousPromotionId.Value, cancellationToken);
            PromotionUsageSupport.DecrementUsage(previousPromotion);
        }

        if (promotion is not null && (!wasAlreadyPriced || previousPromotionId != promotion.Id))
        {
            PromotionUsageSupport.IncrementUsage(promotion);
        }

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            await _boatHoldService.ReleaseAsync(
                booking.Id,
                boat.Id,
                booking.DepartureDate.GetValueOrDefault(),
                booking.StartTime,
                rentalUnit,
                holdDurationValue,
                cancellationToken);
            throw new ValidationException([new ValidationFailure(nameof(request.BoatId),
                "Tàu vừa được giữ cho booking khác. Vui lòng chọn tàu khác hoặc thử lại.")]);
        }

        if (previousBoatId.HasValue
            && previousBoatId.Value != boat.Id)
        {
            await _boatHoldService.ReleaseAsync(
                booking.Id,
                previousBoatId,
                previousDepartureDate.GetValueOrDefault(),
                previousStartTime,
                previousRentalUnit.GetValueOrDefault(),
                previousDurationValue,
                cancellationToken);
        }

        return new QuoteCharterBookingResult(
            booking.Id,
            booking.BookingCode,
            boat.Id,
            boat.Name,
            booking.SubtotalAmount,
            booking.DiscountAmount,
            booking.TotalAmount,
            automaticPricing.UnitPrice,
            chargeableDurationValue,
            request.SubtotalAmount.HasValue ? "Manual" : "Automatic",
            CharterBookingRoutePricingSupport.ToDto(
                automaticPricing.RouteEstimate,
                rentalUnit,
                requestedDurationValue),
            booking.BookingStatus.ToString(),
            booking.PaymentStatus,
            promotion?.PromotionCode);
    }

    private async Task<Promotion?> ResolvePromotionForQuoteAsync(
        Booking booking,
        QuoteCharterBookingCommand request,
        decimal subtotalAmount,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (request.PromotionCode is not null)
        {
            return string.IsNullOrWhiteSpace(request.PromotionCode)
                ? null
                : await CharterBookingPricingSupport.ResolvePromotionAsync(
                    _context,
                    request.PromotionCode,
                    subtotalAmount,
                    now,
                    nameof(request.PromotionCode),
                    validateMinOrder: true,
                    cancellationToken);
        }

        if (!booking.PromotionId.HasValue)
        {
            return null;
        }

        var promotion = booking.Promotion
            ?? await _context.Set<Promotion>()
                .SingleOrDefaultAsync(x => x.Id == booking.PromotionId.Value, cancellationToken);
        if (promotion is null)
        {
            return null;
        }

        CharterBookingPricingSupport.EnsurePromotionCanBeUsed(
            promotion,
            subtotalAmount,
            now,
            nameof(request.PromotionCode),
            validateMinOrder: true);
        return promotion;
    }

    private static void EnsureCanQuote(Booking booking)
    {
        if (booking.BookingStatus is BookingStatus.Cancelled or BookingStatus.Completed
            or BookingStatus.Refunded or BookingStatus.Confirmed)
        {
            throw new ValidationException([new ValidationFailure(nameof(booking.BookingStatus),
                "Không thể chốt giá cho booking đã hủy, đã xác nhận, đã hoàn tất hoặc đã hoàn tiền.")]);
        }

        if (booking.Payments.Any(x =>
                string.Equals(x.PaymentStatus, PendingPaymentStatus, StringComparison.OrdinalIgnoreCase)
                || string.Equals(x.PaymentStatus, PaidPaymentStatus, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ValidationException([new ValidationFailure(nameof(booking.Payments),
                "Booking đã có payment đang chờ hoặc đã thanh toán nên không thể đổi tàu/giá.")]);
        }
    }
}
