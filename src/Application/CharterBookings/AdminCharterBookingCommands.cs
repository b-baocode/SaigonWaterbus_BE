using FluentValidation.Results;
using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common;
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
            .Include(x => x.AssignedManager)
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
                x.Boat != null ? x.Boat.Name : null,
                x.HoldExpiresAt,
                x.AssignedManager == null
                    ? null
                    : new CharterBookingUserAssignmentDto(
                        x.AssignedManager.Id,
                        x.AssignedManager.FullName,
                        x.AssignedManager.UserCode)))
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

    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;
    private readonly IBoatHoldService _boatHoldService;
    private readonly ICharterBookingRealtimeNotifier _realtimeNotifier;

    public UpdateCharterBookingStatusCommandHandler(
        IApplicationDbContext context,
        IUserContext userContext,
        IBoatHoldService? boatHoldService = null,
        ICharterBookingRealtimeNotifier? realtimeNotifier = null)
    {
        _context = context;
        _userContext = userContext;
        _boatHoldService = boatHoldService ?? NullBoatHoldService.Instance;
        _realtimeNotifier = realtimeNotifier ?? NullCharterBookingRealtimeNotifier.Instance;
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
        var previousBoatIds = CharterBookingBoatSelectionSupport.ResolveSelectedBoatIds(booking);
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
        await _realtimeNotifier.PublishChangedAsync(
            new CharterBookingRealtimeEvent(
                booking.Id,
                "StatusChanged",
                booking.BookingStatus.ToString(),
                booking.PaymentStatus),
            cancellationToken);
        if (request.BookingStatus is BookingStatus.Cancelled or BookingStatus.Expired)
        {
            foreach (var previousBoatId in previousBoatIds)
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
        }

        var relatedRoutes = await CharterBookingRoutePricingSupport.LoadRelatedRoutesAsync(
            _context,
            booking,
            cancellationToken);

        return CharterBookingQuerySupport.ToDetailDto(booking, relatedRoutes);
    }

    private static void ApplyStatusUpdate(Booking booking, BookingStatus targetStatus)
    {
        EnsureAdminCanSetStatus(targetStatus);

        if (booking.BookingStatus == targetStatus)
        {
            return;
        }

        switch (targetStatus)
        {
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

            default:
                throw CreateStatusValidation(targetStatus, "Trạng thái charter booking không hợp lệ.");
        }

        booking.BookingStatus = targetStatus;
    }

    private static void EnsureAdminCanSetStatus(BookingStatus targetStatus)
    {
        if (targetStatus is BookingStatus.Cancelled or BookingStatus.Expired or BookingStatus.Completed)
        {
            return;
        }

        var message = targetStatus switch
        {
            BookingStatus.PendingPayment =>
                "Charter booking không dùng trạng thái PendingPayment.",
            BookingStatus.PendingQuote =>
                "PendingQuote do hệ thống gán khi customer tạo yêu cầu thuê tàu.",
            BookingStatus.Quoted =>
                "Quoted do hệ thống gán khi admin chốt giá bằng API quote.",
            BookingStatus.Confirmed =>
                "Confirmed do hệ thống gán khi thanh toán đặt cọc hoặc thanh toán đủ thành công.",
            BookingStatus.Refunded =>
                "Refunded do hệ thống gán sau luồng hoàn tiền thành công.",
            _ => "Trạng thái charter booking không hợp lệ."
        };

        throw CreateStatusValidation(targetStatus,
            $"{message} Admin chỉ được cập nhật thủ công sang Cancelled, Expired hoặc Completed.");
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
    Guid? BoatId = null,
    IReadOnlyList<QuoteCharterBookingBoatRequest>? Boats = null,
    BoatRentalUnit? RentalUnit = null,
    int? DurationValue = null,
    string? PromotionCode = null) : IRequest<QuoteCharterBookingResult>;

public sealed class QuoteCharterBookingCommandValidator : AbstractValidator<QuoteCharterBookingCommand>
{
    public QuoteCharterBookingCommandValidator()
    {
        RuleFor(x => x.BookingId).NotEmpty();
        RuleFor(x => x.BoatId)
            .NotEmpty()
            .When(x => x.BoatId.HasValue);
        RuleFor(x => x.Boats)
            .Must((command, boats) => command.BoatId.HasValue || boats is { Count: > 0 })
            .WithMessage("Cần chọn ít nhất một tàu để chốt giá.");
        RuleFor(x => x.Boats)
            .Must(x => x is null || x.Count <= CharterBookingBoatSelectionSupport.MaxRequestedBoatCount)
            .WithMessage($"Số lượng tàu được chọn không được vượt quá {CharterBookingBoatSelectionSupport.MaxRequestedBoatCount}.");
        RuleFor(x => x.Boats)
            .Must(HaveUniqueBoatOrders)
            .When(x => x.Boats is { Count: > 0 })
            .WithMessage("boatOrder không được trùng.");
        RuleFor(x => x.Boats)
            .Must(HaveUniqueBoatIds)
            .When(x => x.Boats is { Count: > 0 })
            .WithMessage("boatId không được trùng trong cùng một quote.");
        RuleForEach(x => x.Boats).ChildRules(boat =>
        {
            boat.RuleFor(x => x.BoatOrder)
                .GreaterThan(0)
                .WithMessage("boatOrder phải lớn hơn 0.");
            boat.RuleFor(x => x.BoatId).NotEmpty();
        });
        RuleFor(x => x.RentalUnit).IsInEnum().When(x => x.RentalUnit.HasValue);
        RuleFor(x => x.DurationValue)
            .GreaterThan(0)
            .LessThanOrEqualTo(60)
            .When(x => x.DurationValue.HasValue)
            .WithMessage("Thời lượng thuê phải từ 1 đến 60.");
        RuleFor(x => x.PromotionCode).MaximumLength(50).When(x => x.PromotionCode is not null);
    }

    private static bool HaveUniqueBoatOrders(IReadOnlyList<QuoteCharterBookingBoatRequest>? boats) =>
        boats is null || boats.Select(x => x.BoatOrder).Distinct().Count() == boats.Count;

    private static bool HaveUniqueBoatIds(IReadOnlyList<QuoteCharterBookingBoatRequest>? boats) =>
        boats is null || boats.Select(x => x.BoatId).Distinct().Count() == boats.Count;
}

public sealed record PreviewCharterBookingQuoteCommand(
    Guid BookingId,
    Guid? BoatId = null,
    IReadOnlyList<QuoteCharterBookingBoatRequest>? Boats = null,
    BoatRentalUnit? RentalUnit = null,
    int? DurationValue = null,
    string? PromotionCode = null) : IRequest<PreviewCharterBookingQuoteResult>;

public sealed class PreviewCharterBookingQuoteCommandValidator
    : AbstractValidator<PreviewCharterBookingQuoteCommand>
{
    public PreviewCharterBookingQuoteCommandValidator()
    {
        RuleFor(x => x.BookingId).NotEmpty();
        RuleFor(x => x.BoatId)
            .NotEmpty()
            .When(x => x.BoatId.HasValue);
        RuleFor(x => x.Boats)
            .Must((command, boats) => command.BoatId.HasValue || boats is { Count: > 0 })
            .WithMessage("Cần chọn ít nhất một tàu để xem giá.");
        RuleFor(x => x.Boats)
            .Must(x => x is null || x.Count <= CharterBookingBoatSelectionSupport.MaxRequestedBoatCount)
            .WithMessage($"Số lượng tàu được chọn không được vượt quá {CharterBookingBoatSelectionSupport.MaxRequestedBoatCount}.");
        RuleFor(x => x.Boats)
            .Must(HaveUniqueBoatOrders)
            .When(x => x.Boats is { Count: > 0 })
            .WithMessage("boatOrder không được trùng.");
        RuleFor(x => x.Boats)
            .Must(HaveUniqueBoatIds)
            .When(x => x.Boats is { Count: > 0 })
            .WithMessage("boatId không được trùng trong cùng một preview.");
        RuleForEach(x => x.Boats).ChildRules(boat =>
        {
            boat.RuleFor(x => x.BoatOrder)
                .GreaterThan(0)
                .WithMessage("boatOrder phải lớn hơn 0.");
            boat.RuleFor(x => x.BoatId).NotEmpty();
        });
        RuleFor(x => x.RentalUnit).IsInEnum().When(x => x.RentalUnit.HasValue);
        RuleFor(x => x.DurationValue)
            .GreaterThan(0)
            .LessThanOrEqualTo(60)
            .When(x => x.DurationValue.HasValue)
            .WithMessage("Thời lượng thuê phải từ 1 đến 60.");
        RuleFor(x => x.PromotionCode).MaximumLength(50).When(x => x.PromotionCode is not null);
    }

    private static bool HaveUniqueBoatOrders(IReadOnlyList<QuoteCharterBookingBoatRequest>? boats) =>
        boats is null || boats.Select(x => x.BoatOrder).Distinct().Count() == boats.Count;

    private static bool HaveUniqueBoatIds(IReadOnlyList<QuoteCharterBookingBoatRequest>? boats) =>
        boats is null || boats.Select(x => x.BoatId).Distinct().Count() == boats.Count;
}

public sealed class PreviewCharterBookingQuoteCommandHandler
    : IRequestHandler<PreviewCharterBookingQuoteCommand, PreviewCharterBookingQuoteResult>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;
    private readonly TimeProvider _timeProvider;

    public PreviewCharterBookingQuoteCommandHandler(
        IApplicationDbContext context,
        IUserContext userContext,
        TimeProvider timeProvider)
    {
        _context = context;
        _userContext = userContext;
        _timeProvider = timeProvider;
    }

    public async Task<PreviewCharterBookingQuoteResult> Handle(
        PreviewCharterBookingQuoteCommand request,
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

        CharterBookingQuoteSupport.EnsureCanQuote(booking);

        var selectedBoats = await CharterBookingQuoteSupport.LoadSelectedBoatsAsync(
            _context,
            request.BoatId,
            request.Boats,
            cancellationToken);
        CharterBookingQuoteSupport.ValidateSelectedBoats(booking, selectedBoats);

        var rentalUnit = request.RentalUnit ?? booking.RentalUnit.GetValueOrDefault();
        var requestedDurationValue = request.DurationValue ?? booking.DurationValue.GetValueOrDefault();
        var relatedRoutes = await CharterBookingRoutePricingSupport.LoadRelatedRoutesAsync(
            _context,
            booking,
            cancellationToken);
        var selectedBoatPricings = CharterBookingQuoteSupport.EstimateSelectedBoatPrices(
            booking,
            selectedBoats,
            rentalUnit,
            requestedDurationValue,
            relatedRoutes);
        var primarySelection = selectedBoatPricings[0];
        CharterBookingRoutePricingSupport.EnsureCanAutoPrice(rentalUnit, primarySelection.Pricing.RouteEstimate);

        var subtotal = selectedBoatPricings.Sum(x => x.Pricing.SubtotalAmount);
        var now = _timeProvider.GetUtcNow();
        var promotion = await CharterBookingQuoteSupport.ResolvePromotionForQuoteAsync(
            _context,
            booking,
            request.PromotionCode,
            subtotal,
            now,
            nameof(request.PromotionCode),
            cancellationToken);
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

        return new PreviewCharterBookingQuoteResult(
            booking.Id,
            booking.BookingCode,
            CharterBookingQuoteSupport.ToSelectedBoatDtos(selectedBoatPricings),
            subtotal,
            discount,
            total,
            "Automatic",
            CharterBookingRoutePricingSupport.ToDto(
                primarySelection.Pricing.RouteEstimate,
                rentalUnit,
                requestedDurationValue),
            promotion?.PromotionCode);
    }
}

public sealed class QuoteCharterBookingCommandHandler
    : IRequestHandler<QuoteCharterBookingCommand, QuoteCharterBookingResult>
{
    private const string UnpaidBookingPaymentStatus = "Unpaid";

    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;
    private readonly IBoatHoldService _boatHoldService;
    private readonly TimeProvider _timeProvider;
    private readonly ICharterBookingRealtimeNotifier _realtimeNotifier;

    public QuoteCharterBookingCommandHandler(
        IApplicationDbContext context,
        IUserContext userContext,
        TimeProvider timeProvider,
        IBoatHoldService? boatHoldService = null,
        ICharterBookingRealtimeNotifier? realtimeNotifier = null)
    {
        _context = context;
        _userContext = userContext;
        _boatHoldService = boatHoldService ?? NullBoatHoldService.Instance;
        _timeProvider = timeProvider;
        _realtimeNotifier = realtimeNotifier ?? NullCharterBookingRealtimeNotifier.Instance;
    }

    public async Task<QuoteCharterBookingResult> Handle(
        QuoteCharterBookingCommand request,
        CancellationToken cancellationToken)
    {
        await AuthSupport.EnsureCurrentUserIsAdminAsync(_context, _userContext, cancellationToken);

        var booking = await CharterBookingQuerySupport.BuildBaseQuery(_context)
            .Include(x => x.CharterBoats)
                .ThenInclude(x => x.Boat)
            .Include(x => x.Payments)
            .Include(x => x.Promotion)
            .Include(x => x.FromStation)
            .Include(x => x.ToStation)
            .Include(x => x.ItineraryStops)
                .ThenInclude(x => x.Station)
            .SingleOrDefaultAsync(x => x.Id == request.BookingId, cancellationToken)
            ?? throw new NotFoundException("Charter booking not found.");

        CharterBookingQuoteSupport.EnsureCanQuote(booking);

        var selectedBoats = await CharterBookingQuoteSupport.LoadSelectedBoatsAsync(
            _context,
            request.BoatId,
            request.Boats,
            cancellationToken);
        CharterBookingQuoteSupport.ValidateSelectedBoats(booking, selectedBoats);
        var selectedBoatIds = CharterBookingQuoteSupport.GetSelectedBoatIds(selectedBoats);

        var now = _timeProvider.GetUtcNow();

        var conflicts = await CharterBookingQuerySupport.BuildBaseQuery(_context)
            .Include(x => x.CharterBoats)
            .Where(x => x.Id != booking.Id
                && x.DepartureDate == booking.DepartureDate
                && (x.BookingStatus == BookingStatus.Quoted || x.BookingStatus == BookingStatus.Confirmed)
                && ((x.BoatId.HasValue && selectedBoatIds.Contains(x.BoatId.Value))
                    || x.CharterBoats.Any(cb => selectedBoatIds.Contains(cb.BoatId))))
            .ToListAsync(cancellationToken);

        foreach (var conflict in conflicts)
        {
            if (conflict.BookingStatus == BookingStatus.Confirmed
                || (conflict.HoldExpiresAt.HasValue && conflict.HoldExpiresAt.Value > now))
            {
                throw new ValidationException([new ValidationFailure(nameof(request.Boats),
                    "Một trong các tàu đã được giữ cho booking khác trong ngày này.")]);
            }

            conflict.BookingStatus = BookingStatus.Expired;
            conflict.HoldExpiresAt = null;
        }

        var previousBoatIds = CharterBookingBoatSelectionSupport.ResolveSelectedBoatIds(booking);
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
        var selectedBoatPricings = CharterBookingQuoteSupport.EstimateSelectedBoatPrices(
            booking,
            selectedBoats,
            rentalUnit,
            requestedDurationValue,
            relatedRoutes);
        var primarySelection = selectedBoatPricings[0];
        CharterBookingRoutePricingSupport.EnsureCanAutoPrice(rentalUnit, primarySelection.Pricing.RouteEstimate);

        var subtotal = selectedBoatPricings.Sum(x => x.Pricing.SubtotalAmount);
        var chargeableDurationValue = primarySelection.Pricing.ChargeableDurationValue;
        var holdDurationValue = requestedDurationValue;
        var promotion = await CharterBookingQuoteSupport.ResolvePromotionForQuoteAsync(
            _context,
            booking,
            request.PromotionCode,
            subtotal,
            now,
            nameof(request.PromotionCode),
            cancellationToken);
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
        var holdExpiresAt = now + BookingExpirationPolicy.CharterQuoteResponseTtl;

        var holdKeyChanged = previousDepartureDate != booking.DepartureDate
            || previousStartTime != booking.StartTime
            || previousRentalUnit != rentalUnit
            || previousDurationValue != holdDurationValue;
        var newlyHeldBoatIds = new List<Guid>();
        foreach (var selectedBoat in selectedBoatPricings)
        {
            if (await _boatHoldService.TryHoldAsync(
                    booking.Id,
                    selectedBoat.Boat.Id,
                    booking.DepartureDate.GetValueOrDefault(),
                    booking.StartTime,
                    rentalUnit,
                    holdDurationValue,
                    holdExpiresAt,
                    cancellationToken))
            {
                newlyHeldBoatIds.Add(selectedBoat.Boat.Id);
                continue;
            }

            await CharterBookingQuoteSupport.ReleaseQuoteHoldsAsync(
                _boatHoldService,
                booking.Id,
                newlyHeldBoatIds.Where(x => holdKeyChanged || !previousBoatIds.Contains(x)),
                booking.DepartureDate.GetValueOrDefault(),
                booking.StartTime,
                rentalUnit,
                holdDurationValue,
                cancellationToken);
            throw new ValidationException([new ValidationFailure(nameof(request.Boats),
                "Một trong các tàu đang được giữ tạm thời cho booking khác. Vui lòng chọn tàu khác hoặc thử lại sau.")]);
        }

        var primaryBoat = primarySelection.Boat;
        var oldCharterBoats = booking.CharterBoats.ToArray();
        _context.Set<CharterBookingBoat>().RemoveRange(oldCharterBoats);
        booking.CharterBoats.Clear();
        var selectedBoatRows = selectedBoatPricings
            .Select(x => new CharterBookingBoat
            {
                BookingId = booking.Id,
                BoatId = x.Boat.Id,
                BoatOrder = x.BoatOrder,
                SeatSetupType = x.Boat.SeatSetupType,
                UnitPrice = x.Pricing.UnitPrice,
                ChargeableDurationValue = x.Pricing.ChargeableDurationValue,
                SubtotalAmount = x.Pricing.SubtotalAmount,
                Created = now,
                Booking = booking,
                Boat = x.Boat
            })
            .ToArray();
        _context.Set<CharterBookingBoat>().AddRange(selectedBoatRows);

        booking.BoatId = primaryBoat.Id;
        booking.Boat = primaryBoat;
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
            await _realtimeNotifier.PublishChangedAsync(
                new CharterBookingRealtimeEvent(
                    booking.Id,
                    "Quoted",
                    booking.BookingStatus.ToString(),
                    booking.PaymentStatus,
                    now),
                cancellationToken);
        }
        catch (DbUpdateException)
        {
            await CharterBookingQuoteSupport.ReleaseQuoteHoldsAsync(
                _boatHoldService,
                booking.Id,
                newlyHeldBoatIds.Where(x => holdKeyChanged || !previousBoatIds.Contains(x)),
                booking.DepartureDate.GetValueOrDefault(),
                booking.StartTime,
                rentalUnit,
                holdDurationValue,
                cancellationToken);
            throw new ValidationException([new ValidationFailure(nameof(request.Boats),
                "Tàu vừa được giữ cho booking khác. Vui lòng chọn tàu khác hoặc thử lại.")]);
        }

        foreach (var previousBoatId in previousBoatIds)
        {
            if (!holdKeyChanged && selectedBoatIds.Contains(previousBoatId))
            {
                continue;
            }

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
            primaryBoat.Id,
            primaryBoat.Name,
            CharterBookingBoatSelectionSupport.ToSelectedBoatDtos(selectedBoatRows),
            booking.SubtotalAmount,
            booking.DiscountAmount,
            booking.TotalAmount,
            primarySelection.Pricing.UnitPrice,
            chargeableDurationValue,
            "Automatic",
            CharterBookingRoutePricingSupport.ToDto(
                primarySelection.Pricing.RouteEstimate,
                rentalUnit,
                requestedDurationValue),
            booking.BookingStatus.ToString(),
            booking.PaymentStatus,
            promotion?.PromotionCode,
            booking.HoldExpiresAt);
    }
}
