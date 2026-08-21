using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;
using SaigonWaterbus.Application.Common;
using SaigonWaterbus.Application.CharterBookings;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Application.Fares;
using SaigonWaterbus.Application.Payments;
using SaigonWaterbus.Application.Promotions;
using SaigonWaterbus.Application.Seats;
using SaigonWaterbus.Application.Tickets;
using SaigonWaterbus.Application.TicketTypes;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.Bookings;

// FromStationCode/ToStationCode: chặng khách đi — bắt buộc trên chuyến bán vé theo chặng
// (waterbus thường); bỏ trống trên chuyến đi nguyên chuyến (ngắm cảnh vòng lặp), BE tự lấy
// bến đầu → bến cuối của tuyến.
public sealed record BookingItemRequest(
    string? SeatNumber,
    string TicketTypeCode,
    string? FromStationCode,
    string? ToStationCode,
    string PassengerName,
    string? PassengerPhone,
    int? BirthYear,
    string? Gender,
    string? Nationality,
    string? Note,
    string? PassengerEmail = null,
    string? CompanionPassengerName = null);

public sealed record CreateBookingResult(
    Guid BookingId,
    string BookingCode,
    decimal SubtotalAmount,
    decimal TicketSubtotalAmount,
    decimal InsuranceAmount,
    decimal DiscountAmount,
    decimal TotalAmount,
    string BookingStatus,
    int ItemCount,
    DateTimeOffset? HoldExpiresAt,
    Guid? ReturnTripId = null,
    string? ReturnTripCode = null,
    BookingInsuranceDto? Insurance = null);


public sealed record CreateBookingCommand(
    string TripCode,
    IReadOnlyList<BookingItemRequest> Items,
    string? PromotionCode,
    string? ReturnTripCode = null,
    IReadOnlyList<BookingItemRequest>? ReturnItems = null,
    bool? InsuranceSelected = null,
    Guid? InsurancePackageId = null,
    bool? WaterbusInsuranceEnabled = null,
    string? ContactName = null,
    string? ContactPhone = null,
    string? ContactEmail = null) : IRequest<CreateBookingResult>;

public sealed class CreateBookingCommandValidator : AbstractValidator<CreateBookingCommand>
{
    private const string ChildAdultMessage =
        "Booking có vé CHILD phải có ít nhất một ADULT có ghế đi kèm; mỗi INFANT không ghế phải có companionPassengerName khớp với một ADULT có ghế đi kèm cùng chặng.";

    public CreateBookingCommandValidator()
    {
        RuleFor(x => x.TripCode).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Items).NotEmpty().WithMessage("Booking must have at least one item.")
            .Must(items => items.Count <= 10).WithMessage("Maximum 10 items per booking.");

        RuleForEach(x => x.Items).ChildRules(ApplyItemRules);
    
    
        RuleFor(x => x.Items)
            .Must(HasRequiredSeatedAdultCompanions)
            .WithMessage(ChildAdultMessage)
            .When(x => x.Items is not null);

        RuleFor(x => x.ReturnTripCode).MaximumLength(50);
        RuleFor(x => x.ReturnTripCode).NotEmpty()
            .When(x => x.ReturnItems is { Count: > 0 })
            .WithMessage("returnTripCode là bắt buộc khi có returnItems (vé khứ hồi).");
        RuleFor(x => x.ReturnItems).NotEmpty()
            .When(x => !string.IsNullOrWhiteSpace(x.ReturnTripCode))
            .WithMessage("Vé khứ hồi phải có ít nhất một returnItem cho chiều về.");
        RuleFor(x => x.ReturnItems!)
            .Must(items => items.Count <= 10)
            .WithMessage("Maximum 10 items per booking.")
            .When(x => x.ReturnItems is not null);

        RuleForEach(x => x.ReturnItems).ChildRules(ApplyItemRules);

        RuleFor(x => x.ReturnItems!)
            .Must(HasRequiredSeatedAdultCompanions)
            .WithMessage(ChildAdultMessage)
            .When(x => x.ReturnItems is { Count: > 0 });

        RuleFor(x => x.InsurancePackageId)
            .NotEmpty()
            .When(x => x.InsurancePackageId.HasValue);

        RuleFor(x => x.ContactName).MaximumLength(150)
            .When(x => !string.IsNullOrWhiteSpace(x.ContactName));
        RuleFor(x => x.ContactPhone).MaximumLength(20)
            .When(x => !string.IsNullOrWhiteSpace(x.ContactPhone));
        RuleFor(x => x.ContactEmail).EmailAddress().MaximumLength(255)
            .When(x => !string.IsNullOrWhiteSpace(x.ContactEmail));
    }

    private static void ApplyItemRules(InlineValidator<BookingItemRequest> item)
    {
        item.RuleFor(x => x.TicketTypeCode).NotEmpty().MaximumLength(50);
        item.RuleFor(x => x.SeatNumber).MaximumLength(30);
        item.RuleFor(x => x.SeatNumber).NotEmpty()
            .When(x => !IsInfant(x.TicketTypeCode))
            .WithMessage("seatNumber là bắt buộc (chỉ vé INFANT - trẻ dưới 2 tuổi - mới được bỏ trống để ngồi cùng người lớn).");
        item.RuleFor(x => x.BirthYear).NotNull()
            .When(x => RequiresBirthYear(x.TicketTypeCode))
            .WithMessage("birthYear là bắt buộc với vé INFANT/CHILD/SENIOR/DISABLED để khai báo và lưu hành khách.");
       
        item.RuleFor(x => x.FromStationCode).MaximumLength(50);
        item.RuleFor(x => x.ToStationCode).MaximumLength(50);
        item.RuleFor(x => x.PassengerName).NotEmpty().MaximumLength(150);
        item.RuleFor(x => x.PassengerPhone).MaximumLength(20).When(x => x.PassengerPhone is not null);
        item.RuleFor(x => x.PassengerEmail).EmailAddress().MaximumLength(255)
            .When(x => !string.IsNullOrWhiteSpace(x.PassengerEmail));
        item.RuleFor(x => x.CompanionPassengerName).MaximumLength(150);
        item.RuleFor(x => x.CompanionPassengerName).NotEmpty()
            .When(x => IsInfant(x.TicketTypeCode) && string.IsNullOrWhiteSpace(x.SeatNumber))
            .WithMessage("INFANT không ghế bắt buộc companionPassengerName (tên ADULT đi kèm trên form).");
        item.RuleFor(x => x.Note).MaximumLength(500);
    }

    private static bool HasRequiredSeatedAdultCompanions(IReadOnlyList<BookingItemRequest> items)
    {
        var adults = items
            .Where(i => !string.IsNullOrWhiteSpace(i.SeatNumber) && IsAdult(i.TicketTypeCode))
            .ToList();

        if (items.Any(i => IsChild(i.TicketTypeCode)) && adults.Count == 0)
        {
            return false;
        }

        var usedAdultIndexes = new HashSet<int>();
        foreach (var infant in items.Where(i =>
                     string.IsNullOrWhiteSpace(i.SeatNumber) && IsInfant(i.TicketTypeCode)))
        {
            var requestedCompanion = NormalizePassengerName(infant.CompanionPassengerName);
            if (string.IsNullOrWhiteSpace(requestedCompanion))
            {
                return false;
            }

            var adultIndex = -1;
            for (var i = 0; i < adults.Count; i++)
            {
                var adult = adults[i];
                if (!usedAdultIndexes.Contains(i)
                    && SameRequestedSegment(adult, infant)
                    && NormalizePassengerName(adult.PassengerName) == requestedCompanion)
                {
                    adultIndex = i;
                    break;
                }
            }
            if (adultIndex < 0)
            {
                return false;
            }

            usedAdultIndexes.Add(adultIndex);
        }

        return true;
    }

    private static bool IsInfant(string? ticketTypeCode) =>
        string.Equals(ticketTypeCode?.Trim(), "INFANT", StringComparison.OrdinalIgnoreCase);

    private static bool IsChild(string? ticketTypeCode) =>
        string.Equals(ticketTypeCode?.Trim(), "CHILD", StringComparison.OrdinalIgnoreCase);

    private static bool IsSenior(string? ticketTypeCode) =>
        string.Equals(ticketTypeCode?.Trim(), "SENIOR", StringComparison.OrdinalIgnoreCase);

    private static bool IsDisabled(string? ticketTypeCode) =>
        string.Equals(ticketTypeCode?.Trim(), "DISABLED", StringComparison.OrdinalIgnoreCase);

    private static bool RequiresBirthYear(string? ticketTypeCode) =>
        IsInfant(ticketTypeCode) || IsChild(ticketTypeCode) || IsSenior(ticketTypeCode) || IsDisabled(ticketTypeCode);

    private static bool IsAdult(string? ticketTypeCode) =>
        string.Equals(ticketTypeCode?.Trim(), "ADULT", StringComparison.OrdinalIgnoreCase);

    private static bool SameRequestedSegment(BookingItemRequest adult, BookingItemRequest dependent) =>
        string.Equals(adult.FromStationCode?.Trim(), dependent.FromStationCode?.Trim(), StringComparison.OrdinalIgnoreCase)
        && string.Equals(adult.ToStationCode?.Trim(), dependent.ToStationCode?.Trim(), StringComparison.OrdinalIgnoreCase);

    private static string NormalizePassengerName(string? value) =>
        string.Join(' ', (value ?? string.Empty).Trim().ToLowerInvariant().Split(
            ' ',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
}

public sealed class CreateBookingCommandHandler : IRequestHandler<CreateBookingCommand, CreateBookingResult>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;
    private readonly IBookingCodeGenerator _bookingCodeGenerator;
    private readonly IFareCalculator _fareCalculator;
    private readonly TimeProvider _timeProvider;
    private readonly ISeatHoldService _seatHoldService;
    private readonly ITripSeatNotifier _tripSeatNotifier;
    private readonly IPromotionLock? _promotionLock;
    private readonly IPaymentNotificationSender? _paymentNotificationSender;
    private readonly IBookingTicketPdfRenderer? _bookingTicketPdfRenderer;
    private readonly INotificationRealtimeNotifier _notificationRealtimeNotifier;
    private readonly BookingLegResolver _legResolver;

    public CreateBookingCommandHandler(
        IApplicationDbContext context,
        IUserContext userContext,
        IBookingCodeGenerator bookingCodeGenerator,
        IFareCalculator fareCalculator,
        TimeProvider timeProvider,
        ISeatHoldService? seatHoldService = null,
        ITripSeatNotifier? tripSeatNotifier = null,
        IPromotionLock? promotionLock = null,
        IPaymentNotificationSender? paymentNotificationSender = null,
        IBookingTicketPdfRenderer? bookingTicketPdfRenderer = null,
        INotificationRealtimeNotifier? notificationRealtimeNotifier = null)
    {
        _context = context;
        _userContext = userContext;
        _bookingCodeGenerator = bookingCodeGenerator;
        _fareCalculator = fareCalculator;
        _timeProvider = timeProvider;
        _seatHoldService = seatHoldService ?? NullSeatHoldService.Instance;
        _tripSeatNotifier = tripSeatNotifier ?? NullTripSeatNotifier.Instance;
        _promotionLock = promotionLock;
        _paymentNotificationSender = paymentNotificationSender;
        _bookingTicketPdfRenderer = bookingTicketPdfRenderer;
        _notificationRealtimeNotifier = notificationRealtimeNotifier ?? NullNotificationRealtimeNotifier.Instance;
        _legResolver = new BookingLegResolver(_context, _fareCalculator, _seatHoldService);
    }

    public async Task<CreateBookingResult> Handle(CreateBookingCommand request, CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();
        var userId = _userContext.UserId
            ?? throw new ValidationException([new ValidationFailure("userId", "User must be authenticated.")]);

        var user = await _context.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == userId, cancellationToken)
            ?? throw new ValidationException([new ValidationFailure("userId", "User không tồn tại.")]);

        // Email/tên/SĐT trên form đặt vé → Contact* của người đặt vé (gửi QR tổng).
        // Email hành khách chỉ dùng để gửi vé riêng nếu có, không thay thế email người đặt.
        var contactEmail = FirstNonEmpty(request.ContactEmail, user.Email);
        if (string.IsNullOrWhiteSpace(contactEmail))
        {
            throw new ValidationException([new ValidationFailure(nameof(request.ContactEmail),
                "Email liên hệ là bắt buộc để nhận QR/vé điện tử của booking.")]);
        }

        var contactName = FirstNonEmpty(request.ContactName, user.FullName) ?? string.Empty;
        var contactPhone = FirstNonEmpty(request.ContactPhone, user.PhoneNumber) ?? string.Empty;

        var outboundLeg = await _legResolver.ResolveAsync(
            request.TripCode, request.Items, userId, now, nameof(request.TripCode),
            allowDepartedTrip: false, cancellationToken);

        ResolvedLeg? returnLeg = null;
        if (!string.IsNullOrWhiteSpace(request.ReturnTripCode))
        {
            if (request.ReturnItems is not { Count: > 0 })
                throw new ValidationException([new ValidationFailure(nameof(request.ReturnItems),
                    "Vé khứ hồi phải có ít nhất một returnItem cho chiều về.")]);

            returnLeg = await _legResolver.ResolveAsync(
                request.ReturnTripCode, request.ReturnItems, userId, now,
                nameof(request.ReturnTripCode), allowDepartedTrip: false, cancellationToken);

            BookingLegResolver.EnsureLegsDoNotShareSeats(outboundLeg, returnLeg);
            BookingLegResolver.EnsureReturnLegStartsAfterOutboundLeg(
                outboundLeg, returnLeg, nameof(request.ReturnTripCode));
        }
        else if (request.ReturnItems is { Count: > 0 })
        {
            throw new ValidationException([new ValidationFailure(nameof(request.ReturnTripCode),
                "returnTripCode là bắt buộc khi có returnItems (vé khứ hồi).")]);
        }

        var trip = outboundLeg.Trip;
        var legs = new List<ResolvedLeg> { outboundLeg };
        if (returnLeg is not null)
        {
            legs.Add(returnLeg);
        }
        var holdExpiresAt = BookingLegResolver.ResolveHoldExpiresAt(legs, now);

        var ticketSubtotal = outboundLeg.ItemPrices.Sum(x => x.UnitPrice)
            + (returnLeg?.ItemPrices.Sum(x => x.UnitPrice) ?? 0m);
        var insuranceSnapshot = await CharterBookingInsuranceSupport.ResolveSeatBookingInsuranceSnapshotAsync(
            _context,
            request.InsuranceSelected,
            request.InsurancePackageId,
            legs.Sum(x => x.ItemPrices.Count),
            now,
            cancellationToken,
            request.WaterbusInsuranceEnabled);
        var subtotal = PriceRoundingSupport.RoundFare(
            ticketSubtotal + (insuranceSnapshot?.TotalAmount ?? 0m));

        var booking = new Booking
        {
            UserId = userId,
            TripId = trip.Id,
            ReturnTripId = returnLeg?.Trip.Id,
            BookingCode = await _bookingCodeGenerator.GenerateAsync(cancellationToken),
            ContactName = contactName,
            ContactPhone = contactPhone,
            ContactEmail = contactEmail,
            BookingStatus = BookingStatus.PendingPayment,
            SubtotalAmount = subtotal,
            DiscountAmount = 0,
            TotalAmount = subtotal,
            InsuranceSnapshot = insuranceSnapshot,
            HoldExpiresAt = holdExpiresAt
        };

        BookingLegResolver.AddPassengers(booking, legs);

        _context.Set<Booking>().Add(booking);

        var applyContext = new PromotionApplyContext(
            Booking.SeatBookingType,
            trip.RouteId,
            trip.OperatingDate.DayOfWeek,
            TimeOnly.FromTimeSpan(trip.DepartureTime.TimeOfDay));

        try
        {
            await _context.ExecuteInTransactionAsync(
                async ct =>
                {
                    await BookingLegResolver.EnsureSeatsStillAvailableAsync(
                        _context, _legResolver, legs, userId, now, ct);

                    if (!string.IsNullOrWhiteSpace(request.PromotionCode))
                    {
                        var code = PromotionSupport.NormalizeCode(request.PromotionCode);
                        var promotion = _promotionLock is not null
                            ? await _promotionLock.AcquireByCodeAsync(code, ct)
                            : await _context.Set<Promotion>().SingleOrDefaultAsync(p => p.PromotionCode == code, ct);

                        if (promotion is null)
                        {
                            throw new ValidationException([new ValidationFailure(
                                nameof(request.PromotionCode), "Không tìm thấy mã khuyến mãi.")]);
                        }

                        var discount = await PromotionEligibilitySupport.EnsureAndCalculateAsync(
                            _context, promotion, userId, subtotal, now,
                            nameof(request.PromotionCode), applyContext, null, ct);

                        booking.PromotionId = promotion.Id;
                        booking.DiscountAmount = discount;
                        booking.TotalAmount = PriceRoundingSupport.RoundFare(subtotal - discount);
                    }

                    await _context.SaveChangesAsync(ct);
                },
                cancellationToken);
        }
        catch (DbUpdateException)
        {
            throw new ValidationException([new ValidationFailure(nameof(request.Items),
                "Booking failed. Please try again.")]);
        }

        foreach (var leg in legs)
        {
            await BookingLegResolver.NotifyLegBookedAsync(
                leg, userId, _seatHoldService, _tripSeatNotifier, cancellationToken);
        }

        await CompleteFreeRegularBookingAsync(booking, now, cancellationToken);

        return new CreateBookingResult(
            booking.Id, booking.BookingCode,
            booking.SubtotalAmount,
            ticketSubtotal,
            insuranceSnapshot?.TotalAmount ?? 0m,
            booking.DiscountAmount, booking.TotalAmount,
            booking.BookingStatus.ToString(), booking.Passengers.Count,
            booking.HoldExpiresAt,
            booking.ReturnTripId,
            returnLeg?.Trip.TripCode,
            BookingInsuranceDtoMapper.ToDto(booking.InsuranceSnapshot));
    }

    private async Task CompleteFreeRegularBookingAsync(
        Booking booking,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (!PaymentSupport.IsFreeRegularBooking(booking)
            || booking.Payments.Any(x => PaymentSupport.IsSettlementPayment(x) && PaymentSupport.IsPaid(x.PaymentStatus)))
        {
            return;
        }

        var payment = new Payment
        {
            BookingId = booking.Id,
            PaymentCode = await PaymentSupport.GenerateInternalPaymentCodeAsync(
                _context,
                "FREE",
                now,
                cancellationToken),
            Provider = PaymentSupport.FreeProvider,
            Amount = 0,
            Currency = booking.Currency,
            PaymentMethod = PaymentSupport.FreePaymentMethod,
            PaymentPurpose = PaymentSupport.FullPurpose,
            PaymentStatus = PaymentSupport.PendingStatus
        };
        booking.Payments.Add(payment);
        _context.Set<Payment>().Add(payment);

        PaymentSupport.ApplyPaymentStatus(
            booking,
            payment,
            PaymentSupport.PaidStatus,
            paymentLinkId: null,
            checkoutUrl: null,
            now);
        await _context.SaveChangesAsync(cancellationToken);

        if (_paymentNotificationSender is null)
        {
            await TicketIssueSupport.EnsureRegularBookingPassengerTicketsAsync(
                _context,
                booking,
                _timeProvider,
                cancellationToken);
            return;
        }

        await PaymentSupport.SendPaymentNotificationIfPaidAsync(
            _context,
            _timeProvider,
            _paymentNotificationSender,
            booking,
            payment,
            wasPaid: false,
            cancellationToken,
            _bookingTicketPdfRenderer,
            _notificationRealtimeNotifier);
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            var trimmed = value?.Trim();
            if (!string.IsNullOrWhiteSpace(trimmed))
            {
                return trimmed;
            }
        }

        return null;
    }
}
