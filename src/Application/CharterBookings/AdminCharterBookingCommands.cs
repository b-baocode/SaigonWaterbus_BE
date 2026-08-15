using FluentValidation.Results;
using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Application.Notifications;
using SaigonWaterbus.Application.Points;
using SaigonWaterbus.Application.Promotions;
using SaigonWaterbus.Application.Trips;
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
            .AsNoTracking()
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
                        x.AssignedManager.UserCode),
                x.BoatId))
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
    BookingStatus BookingStatus,
    string? Note = null) : IRequest<CharterBookingDetailDto>;

public sealed class UpdateCharterBookingStatusCommandValidator
    : AbstractValidator<UpdateCharterBookingStatusCommand>
{
    public UpdateCharterBookingStatusCommandValidator()
    {
        RuleFor(x => x.BookingId).NotEmpty();
        RuleFor(x => x.BookingStatus).IsInEnum();
        RuleFor(x => x.Note)
            .NotEmpty()
            .When(x => x.BookingStatus == BookingStatus.Cancelled)
            .WithMessage("Lý do hủy charter booking là bắt buộc.");
        RuleFor(x => x.Note).MaximumLength(1000).When(x => x.Note is not null);
    }
}

public sealed class UpdateCharterBookingStatusCommandHandler
    : IRequestHandler<UpdateCharterBookingStatusCommand, CharterBookingDetailDto>
{
    private const string PendingPaymentStatus = "Pending";
    private const string PaidPaymentStatus = BookingPaymentStatusExtensions.PaidValue;

    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;
    private readonly IBoatHoldService _boatHoldService;
    private readonly ICharterBookingRealtimeNotifier _realtimeNotifier;
    private readonly INotificationRealtimeNotifier _notificationRealtimeNotifier;
    private readonly TimeProvider _timeProvider;

    public UpdateCharterBookingStatusCommandHandler(
        IApplicationDbContext context,
        IUserContext userContext,
        IBoatHoldService? boatHoldService = null,
        ICharterBookingRealtimeNotifier? realtimeNotifier = null,
        INotificationRealtimeNotifier? notificationRealtimeNotifier = null,
        TimeProvider? timeProvider = null)
    {
        _context = context;
        _userContext = userContext;
        _boatHoldService = boatHoldService ?? NullBoatHoldService.Instance;
        _realtimeNotifier = realtimeNotifier ?? NullCharterBookingRealtimeNotifier.Instance;
        _notificationRealtimeNotifier = notificationRealtimeNotifier ?? NullNotificationRealtimeNotifier.Instance;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<CharterBookingDetailDto> Handle(
        UpdateCharterBookingStatusCommand request,
        CancellationToken cancellationToken)
    {
        await AuthSupport.EnsureCurrentUserIsAdminAsync(_context, _userContext, cancellationToken);

        var booking = await CharterBookingQuerySupport.BuildDetailQuery(_context)
            .SingleOrDefaultAsync(x => x.Id == request.BookingId, cancellationToken)
            ?? throw new NotFoundException("Charter booking not found.");
        var previousBoatIds = CharterBookingBoatSelectionSupport.ResolveSelectedBoatIds(booking);
        var previousDepartureDate = booking.DepartureDate;
        var previousStartTime = booking.StartTime;
        var previousRentalUnit = booking.RentalUnit;
        var previousDurationValue = booking.DurationValue.GetValueOrDefault();

        ApplyStatusUpdate(booking, request.BookingStatus, request.Note);

        if (request.BookingStatus is BookingStatus.Cancelled or BookingStatus.Expired)
        {
            await CharterBookingTripSupport.CancelLinkedTripsAsync(
                _context,
                booking.Id,
                $"Charter booking {booking.BookingCode} đã bị hủy.",
                cancellationToken);
        }

        if (request.BookingStatus is BookingStatus.Cancelled or BookingStatus.Expired or BookingStatus.Completed)
        {
            await CharterBookingRouteSupport.DeactivateOwnedRouteAsync(
                _context,
                booking,
                cancellationToken);
        }

        if (booking.BookingStatus == BookingStatus.Completed)
        {
            // Charter đã phục vụ xong → tích điểm (idempotent nên gọi lại không cộng trùng).
            await PointSupport.AwardCompletionPointsAsync(
                _context,
                booking,
                _timeProvider.GetUtcNow(),
                cancellationToken);
        }

        // Lượt khuyến mãi suy ra từ bookings — chuyển sang trạng thái nhả lượt là tự cập nhật, không cần bookkeeping.
        await _context.SaveChangesAsync(cancellationToken);

        if (request.BookingStatus == BookingStatus.Cancelled)
        {
            var cancelledNotifications = await NotificationSupport
                .AddCharterBookingCancelledNotificationsAsync(
                    _context,
                    booking,
                    _timeProvider.GetUtcNow(),
                    cancellationToken);
            if (cancelledNotifications.Count > 0)
            {
                await NotificationSupport.PublishCreatedAsync(
                    _notificationRealtimeNotifier,
                    cancelledNotifications,
                    cancellationToken);
            }
        }

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

    private static void ApplyStatusUpdate(Booking booking, BookingStatus targetStatus, string? note)
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
                booking.SpecialRequests = AppendAdminCancellationNote(booking.SpecialRequests, note);
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
            _ => "Trạng thái charter booking không hợp lệ."
        };

        throw CreateStatusValidation(targetStatus,
            $"{message} Admin chỉ được cập nhật thủ công sang Cancelled, Expired hoặc Completed.");
    }

    private static void EnsureCanCancel(Booking booking, BookingStatus targetStatus)
    {
        if (booking.BookingStatus is BookingStatus.Completed or BookingStatus.Cancelled)
        {
            throw CreateStatusValidation(targetStatus,
                "Không thể hủy charter booking đã hoàn tất hoặc đã hủy.");
        }
    }

    private static void EnsureCanExpire(Booking booking, BookingStatus targetStatus)
    {
        if (booking.BookingStatus is BookingStatus.Confirmed or BookingStatus.Completed or BookingStatus.Cancelled)
        {
            throw CreateStatusValidation(targetStatus,
                "Không thể chuyển charter booking đã xác nhận, đã hoàn tất hoặc đã hủy sang Expired.");
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

    private static string? AppendAdminCancellationNote(string? existing, string? note)
    {
        var trimmedNote = note?.Trim();
        if (string.IsNullOrWhiteSpace(trimmedNote))
        {
            return existing;
        }

        var entry = $"Admin hủy booking: {trimmedNote}";
        var value = string.IsNullOrWhiteSpace(existing)
            ? entry
            : $"{existing.Trim()}\n{entry}";

        if (value.Length > 1000)
        {
            throw new ValidationException([new ValidationFailure("note",
                "Lý do hủy vượt quá 1000 ký tự sau khi ghép với ghi chú hiện tại.")]);
        }

        return value;
    }
}

public sealed record QuoteCharterBookingCommand(
    Guid BookingId,
    Guid? BoatId = null,
    IReadOnlyList<QuoteCharterBookingBoatRequest>? Boats = null,
    IReadOnlyList<CharterBookingRoutePlanLegRequest>? RoutePlan = null) : IRequest<QuoteCharterBookingResult>;

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
        RuleFor(x => x.RoutePlan)
            .Must(x => x is null || x.Count > 0)
            .WithMessage("routePlan không được rỗng nếu được gửi.");
        RuleForEach(x => x.RoutePlan).ChildRules(leg =>
        {
            leg.RuleFor(x => x.FromStationId).NotEmpty();
            leg.RuleFor(x => x.ToStationId).NotEmpty();
            leg.RuleFor(x => x.RouteId).NotEmpty();
        });
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
    IReadOnlyList<CharterBookingRoutePlanLegRequest>? RoutePlan = null) : IRequest<PreviewCharterBookingQuoteResult>;

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
        RuleFor(x => x.RoutePlan)
            .Must(x => x is null || x.Count > 0)
            .WithMessage("routePlan không được rỗng nếu được gửi.");
        RuleForEach(x => x.RoutePlan).ChildRules(leg =>
        {
            leg.RuleFor(x => x.FromStationId).NotEmpty();
            leg.RuleFor(x => x.ToStationId).NotEmpty();
            leg.RuleFor(x => x.RouteId).NotEmpty();
        });
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

        var previewRoute = await CharterBookingRoutePlanSupport.ResolveSelectedRouteAsync(
            _context,
            booking,
            request.RoutePlan,
            persist: false,
            _timeProvider.GetUtcNow(),
            cancellationToken);
        var relatedRoutes = previewRoute is null
            ? await CharterBookingRoutePricingSupport.LoadRelatedRoutesAsync(
                _context,
                booking,
                cancellationToken)
            : [previewRoute];
        var rentalUnit = CharterBookingRoutePricingSupport.ResolveRentalUnit(booking);
        var requestedDurationValue = CharterBookingRoutePricingSupport.ResolveRequestedDurationValue(booking);
        var rentalPricePolicies = await CharterBookingRoutePricingSupport.LoadRentalPricePoliciesAsync(
            _context,
            cancellationToken);
        var selectedBoatPricings = CharterBookingQuoteSupport.EstimateSelectedBoatPrices(
            booking,
            selectedBoats,
            rentalUnit,
            requestedDurationValue,
            relatedRoutes,
            rentalPricePolicies);
        var primarySelection = selectedBoatPricings[0];
        CharterBookingRoutePricingSupport.EnsureCanAutoPrice(rentalUnit, primarySelection.Pricing.RouteEstimate);

        var now = _timeProvider.GetUtcNow();
        var insuranceSnapshot = await CharterBookingInsuranceSupport.ResolveQuoteInsuranceSnapshotAsync(
            _context,
            booking.InsuranceSnapshot,
            booking.PassengerCount.GetValueOrDefault(),
            now,
            cancellationToken);
        var subtotal = selectedBoatPricings.Sum(x => x.Pricing.SubtotalAmount)
            + (insuranceSnapshot?.TotalAmount ?? 0);
        var promotion = await CharterBookingQuoteSupport.ResolvePromotionForQuoteAsync(
            _context,
            booking,
            null,
            subtotal,
            now,
            nameof(booking.PromotionId),
            cancellationToken);
        // Per-account/first-booking đã được kiểm tra trong ResolvePromotionForQuoteAsync; chỉ cần đảm bảo có tài khoản.
        if (promotion is not null && !booking.UserId.HasValue)
        {
            throw new ValidationException([new ValidationFailure(
                nameof(booking.UserId),
                "Booking không có tài khoản để kiểm tra promotion.")]);
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
            promotion?.PromotionCode,
            CharterBookingInsuranceSupport.ToDto(insuranceSnapshot),
            CharterBookingRoutePlanSupport.ToSelectedRouteDto(previewRoute));
    }
}

public sealed class QuoteCharterBookingCommandHandler
    : IRequestHandler<QuoteCharterBookingCommand, QuoteCharterBookingResult>
{
    private const string UnpaidBookingPaymentStatus = BookingPaymentStatusExtensions.UnpaidValue;

    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;
    private readonly IBoatHoldService _boatHoldService;
    private readonly TimeProvider _timeProvider;
    private readonly ICharterBookingRealtimeNotifier _realtimeNotifier;
    private readonly INotificationRealtimeNotifier _notificationRealtimeNotifier;

    public QuoteCharterBookingCommandHandler(
        IApplicationDbContext context,
        IUserContext userContext,
        TimeProvider timeProvider,
        IBoatHoldService? boatHoldService = null,
        ICharterBookingRealtimeNotifier? realtimeNotifier = null,
        INotificationRealtimeNotifier? notificationRealtimeNotifier = null)
    {
        _context = context;
        _userContext = userContext;
        _boatHoldService = boatHoldService ?? NullBoatHoldService.Instance;
        _timeProvider = timeProvider;
        _realtimeNotifier = realtimeNotifier ?? NullCharterBookingRealtimeNotifier.Instance;
        _notificationRealtimeNotifier = notificationRealtimeNotifier ?? NullNotificationRealtimeNotifier.Instance;
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
        await EnsureSelectedBoatsAreAvailableAsync(
            booking,
            selectedBoatIds,
            now,
            nameof(request.Boats),
            cancellationToken);

        var previousBoatIds = CharterBookingBoatSelectionSupport.ResolveSelectedBoatIds(booking);
        var previousDepartureDate = booking.DepartureDate;
        var previousStartTime = booking.StartTime;
        var previousRentalUnit = booking.RentalUnit;
        var previousDurationValue = booking.DurationValue.GetValueOrDefault();
        var selectedRoute = await CharterBookingRoutePlanSupport.ResolveSelectedRouteAsync(
            _context,
            booking,
            request.RoutePlan,
            persist: true,
            now,
            cancellationToken);
        var relatedRoutes = selectedRoute is null
            ? await CharterBookingRoutePricingSupport.LoadRelatedRoutesAsync(
                _context,
                booking,
                cancellationToken)
            : [selectedRoute];
        var routeEstimate = CharterBookingRoutePricingSupport.EstimateRoute(booking, relatedRoutes);
        var rentalUnit = CharterBookingRoutePricingSupport.ResolveRentalUnit(booking);
        var requestedDurationValue = CharterBookingRoutePricingSupport.ResolveRequestedDurationValue(booking);
        var rentalPricePolicies = await CharterBookingRoutePricingSupport.LoadRentalPricePoliciesAsync(
            _context,
            cancellationToken);
        var selectedBoatPricings = CharterBookingQuoteSupport.EstimateSelectedBoatPrices(
            booking,
            selectedBoats,
            rentalUnit,
            requestedDurationValue,
            relatedRoutes,
            rentalPricePolicies);
        var primarySelection = selectedBoatPricings[0];
        CharterBookingRoutePricingSupport.EnsureCanAutoPrice(rentalUnit, primarySelection.Pricing.RouteEstimate);

        var insuranceSnapshot = await CharterBookingInsuranceSupport.ResolveQuoteInsuranceSnapshotAsync(
            _context,
            booking.InsuranceSnapshot,
            booking.PassengerCount.GetValueOrDefault(),
            now,
            cancellationToken);
        var subtotal = selectedBoatPricings.Sum(x => x.Pricing.SubtotalAmount)
            + (insuranceSnapshot?.TotalAmount ?? 0);
        var chargeableDurationValue = primarySelection.Pricing.ChargeableDurationValue;
        var holdDurationValue = CharterBookingRoutePricingSupport.ResolveHoldDurationValue(
            rentalUnit,
            requestedDurationValue,
            routeEstimate);
        var promotion = await CharterBookingQuoteSupport.ResolvePromotionForQuoteAsync(
            _context,
            booking,
            null,
            subtotal,
            now,
            nameof(booking.PromotionId),
            cancellationToken);
        // Per-account/first-booking đã được kiểm tra trong ResolvePromotionForQuoteAsync; chỉ cần đảm bảo có tài khoản.
        if (promotion is not null && !booking.UserId.HasValue)
        {
            throw new ValidationException([new ValidationFailure(
                nameof(booking.UserId),
                "Booking không có tài khoản để kiểm tra promotion.")]);
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
        booking.CharterRouteId = selectedRoute?.Id;
        booking.CharterRoute = selectedRoute;
        booking.RentalUnit = rentalUnit;
        booking.DurationValue = holdDurationValue;
        booking.PromotionId = promotion?.Id;
        booking.SubtotalAmount = subtotal;
        booking.DiscountAmount = discount;
        booking.TotalAmount = total;
        booking.InsuranceSnapshot = insuranceSnapshot;
        booking.DepositAmount = 0;
        booking.RemainingAmount = total;
        booking.PaymentStatus = UnpaidBookingPaymentStatus;
        booking.BookingStatus = BookingStatus.Quoted;
        booking.HoldExpiresAt = holdExpiresAt;

        // Lượt/ngân sách khuyến mãi suy ra từ bookings — đổi mã trên booking là tự phản ánh, không cần bookkeeping counter.
        try
        {
            await _context.SaveChangesAsync(cancellationToken);

            var quotedNotification = NotificationSupport.AddCharterBookingQuotedNotification(
                _context,
                booking,
                now);
            if (quotedNotification is not null)
            {
                await _context.SaveChangesAsync(cancellationToken);
                await NotificationSupport.PublishCreatedAsync(
                    _notificationRealtimeNotifier,
                    [quotedNotification],
                    cancellationToken);
            }

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
            booking.HoldExpiresAt,
            CharterBookingInsuranceSupport.ToDto(booking.InsuranceSnapshot),
            CharterBookingRoutePlanSupport.ToSelectedRouteDto(selectedRoute));
    }

    private async Task EnsureSelectedBoatsAreAvailableAsync(
        Booking booking,
        IReadOnlyList<Guid> selectedBoatIds,
        DateTimeOffset now,
        string errorFieldName,
        CancellationToken cancellationToken)
    {
        var requestedWindow = CharterBookingTripSupport.ResolveRentalWindowUtc(booking);
        var bufferedStart = requestedWindow.DepartureTime.Subtract(TripScheduleSupport.BoatTurnaroundBuffer);
        var bufferedEnd = requestedWindow.ArrivalTime.Add(TripScheduleSupport.BoatTurnaroundBuffer);

        var tripConflict = await _context.Set<Trip>()
            .AsNoTracking()
            .Where(x => x.BoatId.HasValue
                && selectedBoatIds.Contains(x.BoatId.Value)
                && x.TripStatus != TripStatus.Cancelled
                && x.DepartureTime < bufferedEnd
                && bufferedStart < x.ArrivalTime)
            .OrderBy(x => x.DepartureTime)
            .Select(x => new { x.TripCode, x.DepartureTime, x.ArrivalTime })
            .FirstOrDefaultAsync(cancellationToken);

        if (tripConflict is not null)
        {
            throw new ValidationException([new ValidationFailure(errorFieldName,
                "Tàu đã có chuyến vào ngày/giờ charter đã chọn: "
                + TripScheduleSupport.BuildConflictMessage(
                    tripConflict.TripCode,
                    tripConflict.DepartureTime,
                    tripConflict.ArrivalTime))]);
        }

        var conflicts = await CharterBookingQuerySupport.BuildBaseQuery(_context)
            .Include(x => x.CharterBoats)
            .Where(x => x.Id != booking.Id
                && (x.BookingStatus == BookingStatus.Quoted || x.BookingStatus == BookingStatus.Confirmed)
                && ((x.BoatId.HasValue && selectedBoatIds.Contains(x.BoatId.Value))
                    || x.CharterBoats.Any(cb => selectedBoatIds.Contains(cb.BoatId))))
            .ToListAsync(cancellationToken);

        foreach (var conflict in conflicts)
        {
            if (conflict.BookingStatus == BookingStatus.Quoted
                && (!conflict.HoldExpiresAt.HasValue || conflict.HoldExpiresAt.Value <= now))
            {
                conflict.BookingStatus = BookingStatus.Expired;
                conflict.HoldExpiresAt = null;
                await PointSupport.ReturnRedeemedPointsAsync(
                    _context,
                    conflict,
                    $"Hoàn điểm do charter booking {conflict.BookingCode} hết hạn giữ tàu",
                    now,
                    cancellationToken);
                continue;
            }

            var conflictWindow = CharterBookingTripSupport.ResolveRentalWindowUtc(conflict);
            if (!CharterBookingTripSupport.HasScheduleOverlap(
                    bufferedStart,
                    bufferedEnd,
                    conflictWindow.DepartureTime,
                    conflictWindow.ArrivalTime))
            {
                continue;
            }

            throw new ValidationException([new ValidationFailure(errorFieldName,
                $"Tàu đã được giữ/xác nhận cho charter booking {conflict.BookingCode} "
                + $"trong khung {CharterBookingTripSupport.FormatVietnamWindow(conflictWindow)}. "
                + "Vui lòng chọn tàu khác hoặc đổi giờ.")]);
        }
    }
}
