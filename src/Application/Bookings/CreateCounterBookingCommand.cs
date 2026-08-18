using System.Globalization;
using FluentValidation.Results;
using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common;
using SaigonWaterbus.Application.CharterBookings;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Application.Notifications;
using SaigonWaterbus.Application.Payments;
using SaigonWaterbus.Application.Points;
using SaigonWaterbus.Application.Seats;
using SaigonWaterbus.Application.Tickets;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.Bookings;

public enum CounterPaymentMethod
{
    /// <summary>Staff thu tiền mặt tại quầy — booking xác nhận và phát hành vé ngay.</summary>
    Cash = 0,

    /// <summary>Khách quét QR PayOS tại quầy — vé chỉ phát hành sau khi cổng báo đã thanh toán.</summary>
    PayOs = 2
}

public sealed record CounterBookingResult(
    Guid BookingId,
    string BookingCode,
    decimal SubtotalAmount,
    decimal TotalAmount,
    string BookingStatus,
    string PaymentStatus,
    int ItemCount,
    DateTimeOffset? HoldExpiresAt,
    Guid? PaymentId,
    string? PaymentCode,
    string PaymentMethod,
    string? CheckoutUrl,
    string? QrCode,
    DateTimeOffset? PaidAt,
    Guid? ReturnTripId = null,
    string? ReturnTripCode = null,
    BookingInsuranceDto? Insurance = null,
    BookingManifestDto? Manifest = null);

/// <summary>
/// Bán vé tại quầy: staff/manager đặt hộ khách trực tiếp và thu tiền ngay.
/// Nếu khách xác nhận tài khoản đã lookup, booking được gắn user để tích điểm sau khi hoàn tất dịch vụ;
/// nếu không thì giữ dạng khách vãng lai.
/// </summary>
public sealed record CreateCounterBookingCommand(
    string TripCode,
    IReadOnlyList<BookingItemRequest> Items,
    string ContactName,
    string ContactPhone,
    string? ContactEmail,
    CounterPaymentMethod PaymentMethod = CounterPaymentMethod.Cash,
    string? ReturnTripCode = null,
    IReadOnlyList<BookingItemRequest>? ReturnItems = null,
    bool? InsuranceSelected = null,
    Guid? InsurancePackageId = null,
    Guid? CustomerUserId = null,
    bool CustomerConfirmedForPoints = false,
    bool UseAllPoints = false) : IRequest<CounterBookingResult>;

public sealed class CreateCounterBookingCommandValidator : AbstractValidator<CreateCounterBookingCommand>
{
    public CreateCounterBookingCommandValidator()
    {
        RuleFor(x => x.TripCode).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Items).NotEmpty().WithMessage("Booking must have at least one item.")
            .Must(items => items.Count <= 10).WithMessage("Maximum 10 items per booking.");
        RuleFor(x => x.ContactName).NotEmpty().MaximumLength(150);
        RuleFor(x => x.ContactPhone).NotEmpty().MaximumLength(20);
        RuleFor(x => x.ContactEmail)
            .EmailAddress()
            .MaximumLength(255)
            .When(x => !string.IsNullOrWhiteSpace(x.ContactEmail));
        RuleFor(x => x.PaymentMethod).IsInEnum();
        RuleFor(x => x.ReturnTripCode).MaximumLength(50);
        RuleFor(x => x.ReturnItems!)
            .Must(items => items.Count <= 10)
            .WithMessage("Maximum 10 items per booking.")
            .When(x => x.ReturnItems is not null);
        RuleFor(x => x.InsurancePackageId)
            .NotEmpty()
            .When(x => x.InsurancePackageId.HasValue);
        RuleFor(x => x.CustomerUserId)
            .NotEmpty()
            .When(x => x.CustomerUserId.HasValue);
    }
}

public sealed class CreateCounterBookingCommandHandler
    : IRequestHandler<CreateCounterBookingCommand, CounterBookingResult>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;
    private readonly IBookingCodeGenerator _bookingCodeGenerator;
    private readonly TimeProvider _timeProvider;
    private readonly ISeatHoldService _seatHoldService;
    private readonly ITripSeatNotifier _tripSeatNotifier;
    private readonly ISender _sender;
    private readonly IPaymentNotificationSender _paymentNotificationSender;
    private readonly IBookingTicketPdfRenderer? _bookingTicketPdfRenderer;
    private readonly INotificationRealtimeNotifier _notificationRealtimeNotifier;
    private readonly BookingLegResolver _legResolver;

    public CreateCounterBookingCommandHandler(
        IApplicationDbContext context,
        IUserContext userContext,
        IBookingCodeGenerator bookingCodeGenerator,
        IFareCalculator fareCalculator,
        TimeProvider timeProvider,
        ISender sender,
        IPaymentNotificationSender paymentNotificationSender,
        ISeatHoldService? seatHoldService = null,
        ITripSeatNotifier? tripSeatNotifier = null,
        IBookingTicketPdfRenderer? bookingTicketPdfRenderer = null,
        INotificationRealtimeNotifier? notificationRealtimeNotifier = null)
    {
        _context = context;
        _userContext = userContext;
        _bookingCodeGenerator = bookingCodeGenerator;
        _timeProvider = timeProvider;
        _seatHoldService = seatHoldService ?? NullSeatHoldService.Instance;
        _tripSeatNotifier = tripSeatNotifier ?? NullTripSeatNotifier.Instance;
        _sender = sender;
        _paymentNotificationSender = paymentNotificationSender;
        _bookingTicketPdfRenderer = bookingTicketPdfRenderer;
        _notificationRealtimeNotifier = notificationRealtimeNotifier ?? NullNotificationRealtimeNotifier.Instance;
        _legResolver = new BookingLegResolver(_context, fareCalculator, _seatHoldService);
    }

    public async Task<CounterBookingResult> Handle(
        CreateCounterBookingCommand request,
        CancellationToken cancellationToken)
    {
        var seller = await AuthSupport.EnsureCurrentUserCanSellAtCounterAsync(_context, _userContext, cancellationToken);
        var now = _timeProvider.GetUtcNow();
        var loyaltyCustomer = await ResolveCounterBookingCustomerAsync(
            request.CustomerUserId,
            request.CustomerConfirmedForPoints,
            cancellationToken);

        // allowDepartedTrip: quầy vé bán được cả khi tàu đã rời bến (khách lên ở bến giữa tuyến),
        // chỉ chặn chuyến đã kết thúc/hủy. Pre-hold ghế tính theo người bán đang thao tác.
        var outboundLeg = await _legResolver.ResolveAsync(
            request.TripCode, request.Items, seller.Id, now, nameof(request.TripCode),
            allowDepartedTrip: true, cancellationToken);

        ResolvedLeg? returnLeg = null;
        if (!string.IsNullOrWhiteSpace(request.ReturnTripCode))
        {
            if (request.ReturnItems is not { Count: > 0 })
                throw new ValidationException([new ValidationFailure(nameof(request.ReturnItems),
                    "Vé khứ hồi phải có ít nhất một returnItem cho chiều về.")]);

            returnLeg = await _legResolver.ResolveAsync(
                request.ReturnTripCode, request.ReturnItems, seller.Id, now,
                nameof(request.ReturnTripCode), allowDepartedTrip: true, cancellationToken);

            BookingLegResolver.EnsureLegsDoNotShareSeats(outboundLeg, returnLeg);
            BookingLegResolver.EnsureReturnLegStartsAfterOutboundLeg(
                outboundLeg, returnLeg, nameof(request.ReturnTripCode));
        }
        else if (request.ReturnItems is { Count: > 0 })
        {
            throw new ValidationException([new ValidationFailure(nameof(request.ReturnTripCode),
                "returnTripCode là bắt buộc khi có returnItems (vé khứ hồi).")]);
        }

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
            cancellationToken);
        var subtotal = PriceRoundingSupport.RoundFare(
            ticketSubtotal + (insuranceSnapshot?.TotalAmount ?? 0m));
        EnsureCounterPointsRequestIsValid(request, loyaltyCustomer, subtotal);

        // Nếu quầy đã lookup và khách xác nhận tài khoản, gắn UserId để tích điểm sau khi hoàn tất dịch vụ.
        // Vẫn không dùng khuyến mãi tại quầy; khách vãng lai giữ UserId null như trước.
        var booking = new Booking
        {
            UserId = loyaltyCustomer?.Id,
            SoldByStaffId = seller.Id,
            TripId = outboundLeg.Trip.Id,
            ReturnTripId = returnLeg?.Trip.Id,
            BookingCode = await _bookingCodeGenerator.GenerateAsync(cancellationToken),
            ContactName = request.ContactName.Trim(),
            ContactPhone = request.ContactPhone.Trim(),
            ContactEmail = NormalizeOptionalEmail(request.ContactEmail),
            BookingStatus = BookingStatus.PendingPayment,
            SubtotalAmount = subtotal,
            DiscountAmount = 0,
            TotalAmount = subtotal,
            RemainingAmount = subtotal,
            InsuranceSnapshot = insuranceSnapshot,
            HoldExpiresAt = holdExpiresAt
        };

        BookingLegResolver.AddPassengers(booking, legs);
        _context.Set<Booking>().Add(booking);

        try
        {
            await _context.ExecuteInTransactionAsync(
                async ct =>
                {
                    // Chốt ghế: khoá trip_seats rồi kiểm tra lại trước khi insert — chặn quầy và
                    // khách online cùng bán một ghế trong cùng khoảnh khắc.
                    await BookingLegResolver.EnsureSeatsStillAvailableAsync(
                        _context, _legResolver, legs, seller.Id, now, ct);

                    await _context.SaveChangesAsync(ct);

                    if (request.UseAllPoints && loyaltyCustomer != null)
                    {
                        var maxRedeemable = PointSupport.CalculateMaxRedeemablePoints(subtotal);
                        var pointsToUse = Math.Min(loyaltyCustomer.PointBalance, maxRedeemable);
                        if (pointsToUse > 0)
                        {
                            await PaymentSupport.ApplyPointsForCheckoutAsync(
                                _context, booking, pointsToUse, now, ct);
                        }
                    }
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
                leg, seller.Id, _seatHoldService, _tripSeatNotifier, cancellationToken);
        }

        // Đơn 0đ không qua được cổng thanh toán,
        // luôn ghi nhận như thu tại quầy để vé vẫn được phát hành.
        var counterPaymentMethod = booking.TotalAmount <= 0
            ? CounterPaymentMethod.Cash
            : request.PaymentMethod;
        var settleAtCounter = counterPaymentMethod is CounterPaymentMethod.Cash;
        var payment = settleAtCounter
            ? await SettleAtCounterAsync(booking, now, cancellationToken)
            : null;

        if (payment is null)
        {
            var payOsPayment = await _sender.Send(new CreatePaymentCommand(booking.Id), cancellationToken);
            return ToResult(booking, returnLeg, CounterPaymentMethod.PayOs.ToString(), payOsPayment);
        }

        return ToResult(
            booking,
            returnLeg,
            payment.PaymentMethod,
            payment,
            await BuildPrintableManifestAsync(booking.Id, now, cancellationToken));
    }

    private async Task<User?> ResolveCounterBookingCustomerAsync(
        Guid? customerUserId,
        bool customerConfirmedForPoints,
        CancellationToken cancellationToken)
    {
        if (!customerUserId.HasValue)
        {
            if (customerConfirmedForPoints)
            {
                throw new ValidationException([new ValidationFailure(
                    nameof(CreateCounterBookingCommand.CustomerConfirmedForPoints),
                    "Không thể xác nhận tích điểm khi chưa chọn tài khoản khách hàng.")]);
            }

            return null;
        }

        if (!customerConfirmedForPoints)
        {
            throw new ValidationException([new ValidationFailure(
                nameof(CreateCounterBookingCommand.CustomerConfirmedForPoints),
                "Staff phải xác nhận đúng tài khoản khách hàng trước khi tích điểm.")]);
        }

        var customer = await _context.Set<User>()
            .Include(u => u.Role)
            .SingleOrDefaultAsync(u => u.Id == customerUserId.Value, cancellationToken);

        if (customer is null || customer.Status != UserStatus.Active || !AuthSupport.IsCustomer(customer))
        {
            throw new ValidationException([new ValidationFailure(nameof(CreateCounterBookingCommand.CustomerUserId),
                "Tài khoản khách hàng không hợp lệ hoặc không còn hoạt động.")]);
        }

        return customer;
    }

    private static void EnsureCounterPointsRequestIsValid(
        CreateCounterBookingCommand request,
        User? loyaltyCustomer,
        decimal subtotal)
    {
        if (!request.UseAllPoints)
        {
            return;
        }

        if (loyaltyCustomer is null)
        {
            throw new ValidationException([new ValidationFailure(nameof(CreateCounterBookingCommand.UseAllPoints),
                "Chỉ dùng điểm khi đã chọn và xác nhận tài khoản khách hàng.")]);
        }

        var maxRedeemable = PointSupport.CalculateMaxRedeemablePoints(subtotal);
        if (loyaltyCustomer.PointBalance <= 0 && maxRedeemable <= 0)
        {
            throw new ValidationException([new ValidationFailure(nameof(CreateCounterBookingCommand.UseAllPoints),
                "Khách hàng không có điểm khả dụng.")]);
        }
    }

    /// <summary>
    /// Thu tại quầy: ghi nhận payment đã thanh toán ngay (không qua cổng) rồi chạy đúng nhánh
    /// hậu-thanh-toán của booking online — xác nhận booking, phát hành vé/QR và gửi email vé điện tử nếu có email.
    /// </summary>
    private async Task<Payment> SettleAtCounterAsync(
        Booking booking,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var orderCode = await PaymentSupport.GeneratePaymentOrderCodeAsync(_context, now, cancellationToken);
        var payment = new Payment
        {
            BookingId = booking.Id,
            PaymentCode = orderCode.ToString(CultureInfo.InvariantCulture),
            Provider = PaymentSupport.CounterProvider,
            Amount = booking.TotalAmount,
            Currency = booking.Currency,
            PaymentMethod = PaymentSupport.CashPaymentMethod,
            PaymentPurpose = PaymentSupport.FullPurpose,
            PaymentStatus = PaymentSupport.PendingStatus
        };
        booking.Payments.Add(payment);
        _context.Set<Payment>().Add(payment);

        PaymentSupport.ApplyPaymentStatus(
            booking, payment, PaymentSupport.PaidStatus, paymentLinkId: null, checkoutUrl: null, now);
        await _context.SaveChangesAsync(cancellationToken);

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

        return payment;
    }

    private async Task<BookingManifestDto> BuildPrintableManifestAsync(
        Guid bookingId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var printableBooking = await BookingManifestSupport.GetByIdAsync(_context, bookingId, cancellationToken);
        return BookingManifestSupport.ToDto(printableBooking, now);
    }

    private static string? NormalizeOptionalEmail(string? email) =>
        string.IsNullOrWhiteSpace(email) ? null : email.Trim();

    private static CounterBookingResult ToResult(
        Booking booking,
        ResolvedLeg? returnLeg,
        string paymentMethod,
        Payment payment,
        BookingManifestDto? manifest = null) =>
        new(
            booking.Id, booking.BookingCode,
            booking.SubtotalAmount, booking.TotalAmount,
            booking.BookingStatus.ToString(), booking.PaymentStatus,
            booking.Passengers.Count, booking.HoldExpiresAt,
            payment.Id, payment.PaymentCode, paymentMethod,
            payment.CheckoutUrl, payment.QrCode, payment.PaidAt,
            booking.ReturnTripId,
            returnLeg?.Trip.TripCode,
            BookingInsuranceDtoMapper.ToDto(booking.InsuranceSnapshot),
            manifest);

    private static CounterBookingResult ToResult(
        Booking booking,
        ResolvedLeg? returnLeg,
        string paymentMethod,
        PaymentDto payment) =>
        new(
            booking.Id, booking.BookingCode,
            booking.SubtotalAmount, booking.TotalAmount,
            booking.BookingStatus.ToString(), booking.PaymentStatus,
            booking.Passengers.Count, booking.HoldExpiresAt,
            payment.PaymentId, payment.PaymentCode, paymentMethod,
            payment.CheckoutUrl, payment.QrCode, payment.PaidAt,
            booking.ReturnTripId,
            returnLeg?.Trip.TripCode,
            BookingInsuranceDtoMapper.ToDto(booking.InsuranceSnapshot));
}
