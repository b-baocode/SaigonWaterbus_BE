using System.Globalization;
using FluentValidation.Results;
using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.CharterBookings;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Application.Notifications;
using SaigonWaterbus.Application.Payments;
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
    PayOs = 1
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
    BookingInsuranceDto? Insurance = null);

/// <summary>
/// Bán vé tại quầy: staff đặt hộ khách vãng lai (không cần tài khoản) và thu tiền ngay.
/// Khác booking online ở ba điểm: booking không gắn user (chỉ lưu thông tin liên hệ staff nhập),
/// bán được cả khi tàu đã rời bến, và có thể thu tiền mặt để xác nhận tức thì.
/// </summary>
public sealed record CreateCounterBookingCommand(
    string TripCode,
    IReadOnlyList<BookingItemRequest> Items,
    string ContactName,
    string ContactPhone,
    string ContactEmail,
    CounterPaymentMethod PaymentMethod = CounterPaymentMethod.Cash,
    string? ReturnTripCode = null,
    IReadOnlyList<BookingItemRequest>? ReturnItems = null,
    bool? InsuranceSelected = null,
    Guid? InsurancePackageId = null) : IRequest<CounterBookingResult>;

public sealed class CreateCounterBookingCommandValidator : AbstractValidator<CreateCounterBookingCommand>
{
    public CreateCounterBookingCommandValidator()
    {
        RuleFor(x => x.TripCode).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Items).NotEmpty().WithMessage("Booking must have at least one item.")
            .Must(items => items.Count <= 10).WithMessage("Maximum 10 items per booking.");
        RuleFor(x => x.ContactName).NotEmpty().MaximumLength(150);
        RuleFor(x => x.ContactPhone).NotEmpty().MaximumLength(20);
        RuleFor(x => x.ContactEmail).NotEmpty().EmailAddress().MaximumLength(255);
        RuleFor(x => x.PaymentMethod).IsInEnum();
        RuleFor(x => x.ReturnTripCode).MaximumLength(50);
        RuleFor(x => x.ReturnItems!)
            .Must(items => items.Count <= 10)
            .WithMessage("Maximum 10 items per booking.")
            .When(x => x.ReturnItems is not null);
        RuleFor(x => x.InsurancePackageId)
            .NotEmpty()
            .When(x => x.InsurancePackageId.HasValue);
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
        var staff = await AuthSupport.EnsureCurrentUserIsStaffAsync(_context, _userContext, cancellationToken);
        var now = _timeProvider.GetUtcNow();

        // allowDepartedTrip: quầy vé bán được cả khi tàu đã rời bến (khách lên ở bến giữa tuyến),
        // chỉ chặn chuyến đã kết thúc/hủy. Pre-hold ghế tính theo staff đang thao tác.
        var outboundLeg = await _legResolver.ResolveAsync(
            request.TripCode, request.Items, staff.Id, now, nameof(request.TripCode),
            allowDepartedTrip: true, cancellationToken);

        ResolvedLeg? returnLeg = null;
        if (!string.IsNullOrWhiteSpace(request.ReturnTripCode))
        {
            if (request.ReturnItems is not { Count: > 0 })
                throw new ValidationException([new ValidationFailure(nameof(request.ReturnItems),
                    "Vé khứ hồi phải có ít nhất một returnItem cho chiều về.")]);

            returnLeg = await _legResolver.ResolveAsync(
                request.ReturnTripCode, request.ReturnItems, staff.Id, now,
                nameof(request.ReturnTripCode), allowDepartedTrip: true, cancellationToken);

            BookingLegResolver.EnsureLegsDoNotShareSeats(outboundLeg, returnLeg);
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
        var subtotal = ticketSubtotal + (insuranceSnapshot?.TotalAmount ?? 0m);

        // Khách vãng lai: booking không gắn user nên không tích/dùng điểm và không áp mã khuyến mãi
        // (mã khuyến mãi kiểm tra hạn mức theo từng tài khoản).
        var booking = new Booking
        {
            UserId = null,
            SoldByStaffId = staff.Id,
            TripId = outboundLeg.Trip.Id,
            ReturnTripId = returnLeg?.Trip.Id,
            BookingCode = await _bookingCodeGenerator.GenerateAsync(cancellationToken),
            ContactName = request.ContactName.Trim(),
            ContactPhone = request.ContactPhone.Trim(),
            ContactEmail = request.ContactEmail.Trim(),
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
                        _context, _legResolver, legs, staff.Id, now, ct);

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
                leg, staff.Id, _seatHoldService, _tripSeatNotifier, cancellationToken);
        }

        // Đơn 0đ không qua được cổng thanh toán,
        // luôn ghi nhận như thu tại quầy để vé vẫn được phát hành.
        var settleAtCounter = request.PaymentMethod == CounterPaymentMethod.Cash || booking.TotalAmount <= 0;
        var payment = settleAtCounter
            ? await SettleWithCashAsync(booking, now, cancellationToken)
            : null;

        if (payment is null)
        {
            var payOsPayment = await _sender.Send(new CreatePaymentCommand(booking.Id), cancellationToken);
            return ToResult(booking, returnLeg, CounterPaymentMethod.PayOs.ToString(), payOsPayment);
        }

        return ToResult(booking, returnLeg, payment.PaymentMethod, payment);
    }

    /// <summary>
    /// Thu tiền mặt: ghi nhận payment đã thanh toán ngay (không qua cổng) rồi chạy đúng nhánh
    /// hậu-thanh-toán của booking online — xác nhận booking, phát hành vé/QR và gửi email vé điện tử.
    /// </summary>
    private async Task<Payment> SettleWithCashAsync(
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

    private static CounterBookingResult ToResult(
        Booking booking,
        ResolvedLeg? returnLeg,
        string paymentMethod,
        Payment payment) =>
        new(
            booking.Id, booking.BookingCode,
            booking.SubtotalAmount, booking.TotalAmount,
            booking.BookingStatus.ToString(), booking.PaymentStatus,
            booking.Passengers.Count, booking.HoldExpiresAt,
            payment.Id, payment.PaymentCode, paymentMethod,
            payment.CheckoutUrl, payment.QrCode, payment.PaidAt,
            booking.ReturnTripId,
            returnLeg?.Trip.TripCode,
            BookingInsuranceDtoMapper.ToDto(booking.InsuranceSnapshot));

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
