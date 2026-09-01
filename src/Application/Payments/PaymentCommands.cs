using System.Globalization;
using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common;
using FluentValidation.Results;
using SaigonWaterbus.Application.Bookings;
using SaigonWaterbus.Application.CharterBookings;
using SaigonWaterbus.Application.Common.Exceptions;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Application.Notifications;
using SaigonWaterbus.Application.Points;
using SaigonWaterbus.Application.Promotions;
using SaigonWaterbus.Application.Tickets;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.Payments;

public sealed record CreatePaymentCommand(
    Guid BookingId,
    BookingPaymentOption PaymentOption = BookingPaymentOption.Full,
    decimal? DepositPercent = null,
    string? PromotionCode = null,
    int? PointsToUse = null)
    : IRequest<PaymentDto>;

public sealed class CreatePaymentCommandValidator : AbstractValidator<CreatePaymentCommand>
{
    public CreatePaymentCommandValidator()
    {
        RuleFor(x => x.BookingId).NotEmpty();
        RuleFor(x => x.PaymentOption).IsInEnum();
        RuleFor(x => x.DepositPercent)
            .GreaterThan(0)
            .LessThan(100)
            .When(x => x.PaymentOption == BookingPaymentOption.Deposit && x.DepositPercent.HasValue)
            .WithMessage("Phần trăm đặt cọc phải lớn hơn 0 và nhỏ hơn 100.");
        RuleFor(x => x.PromotionCode)
            .MaximumLength(50)
            .When(x => x.PromotionCode is not null);
        RuleFor(x => x.PointsToUse)
            .GreaterThanOrEqualTo(0)
            .When(x => x.PointsToUse.HasValue)
            .WithMessage("Số điểm sử dụng không được âm.");
    }
}

public sealed class CreatePaymentCommandHandler : IRequestHandler<CreatePaymentCommand, PaymentDto>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;
    private readonly ICharterBookingPaymentGateway _paymentGateway;
    private readonly IPaymentNotificationSender _paymentNotificationSender;
    private readonly IPaymentProcessingLock _paymentProcessingLock;
    private readonly ITripSeatNotifier _tripSeatNotifier;
    private readonly TimeProvider _timeProvider;
    private readonly IBookingTicketPdfRenderer? _bookingTicketPdfRenderer;
    private readonly INotificationRealtimeNotifier _notificationRealtimeNotifier;
    private readonly ICharterBookingTicketPdfRenderer? _charterBookingTicketPdfRenderer;

    public CreatePaymentCommandHandler(
        IApplicationDbContext context,
        IUserContext userContext,
        ICharterBookingPaymentGateway paymentGateway,
        IPaymentNotificationSender paymentNotificationSender,
        TimeProvider timeProvider,
        IPaymentProcessingLock? paymentProcessingLock = null,
        ITripSeatNotifier? tripSeatNotifier = null,
        IBookingTicketPdfRenderer? bookingTicketPdfRenderer = null,
        INotificationRealtimeNotifier? notificationRealtimeNotifier = null,
        ICharterBookingTicketPdfRenderer? charterBookingTicketPdfRenderer = null)
    {
        _context = context;
        _userContext = userContext;
        _paymentGateway = paymentGateway;
        _paymentNotificationSender = paymentNotificationSender;
        _paymentProcessingLock = paymentProcessingLock ?? NullPaymentProcessingLock.Instance;
        _tripSeatNotifier = tripSeatNotifier ?? NullTripSeatNotifier.Instance;
        _timeProvider = timeProvider;
        _bookingTicketPdfRenderer = bookingTicketPdfRenderer;
        _notificationRealtimeNotifier = notificationRealtimeNotifier ?? NullNotificationRealtimeNotifier.Instance;
        _charterBookingTicketPdfRenderer = charterBookingTicketPdfRenderer;
    }

    public async Task<PaymentDto> Handle(CreatePaymentCommand request, CancellationToken cancellationToken)
    {
        await using var paymentLock = await _paymentProcessingLock.TryAcquireAsync(
            $"booking:{request.BookingId:N}",
            cancellationToken);
        if (!paymentLock.Acquired)
        {
            throw new ValidationException([new ValidationFailure("payment",
                "Booking đang được xử lý thanh toán. Vui lòng thử lại sau.")]);
        }

        var booking = await PaymentSupport.GetOwnedBookingAsync(
            _context,
            _userContext,
            request.BookingId,
            includePayments: true,
            cancellationToken);

        var now = _timeProvider.GetUtcNow();
        if (PaymentSupport.ExpireStalePendingPayments(booking, now))
        {
            await _context.SaveChangesAsync(cancellationToken);
        }

        PaymentSupport.EnsureCanCreatePayment(booking, now);

        if (BookingSeatOccupancySupport.IsHoldExpired(booking, now))
        {
            await BookingHoldExpirySupport.ExpireBookingAsync(
                _context,
                _tripSeatNotifier,
                booking,
                now,
                cancellationToken);
            throw new ValidationException([new ValidationFailure("booking",
                "Booking đã hết hạn giữ chỗ. Vui lòng đặt vé lại.")]);
        }

        await PaymentSupport.ApplyPromotionForCheckoutAsync(
            _context,
            _userContext,
            booking,
            request.PromotionCode,
            now,
            cancellationToken);

        await PaymentSupport.ApplyPointsForCheckoutAsync(
            _context,
            booking,
            request.PointsToUse,
            now,
            cancellationToken);

        if (booking.TotalAmount == 0)
        {
            return await CompleteZeroAmountBookingAsync(booking, now, cancellationToken);
        }

        var existingPendingPayment = booking.Payments
            .Where(PaymentSupport.IsPayOsPayment)
            .OrderByDescending(x => x.Created)
            .FirstOrDefault(x =>
                PaymentSupport.IsPending(x.PaymentStatus)
                && !PaymentSupport.IsExpired(x, now)
                && !string.IsNullOrWhiteSpace(x.CheckoutUrl));
        if (existingPendingPayment is not null)
        {
            return PaymentSupport.ToDto(booking, existingPendingPayment);
        }

        var paidAmount = PaymentSupport.GetPaidAmount(booking);
        if (paidAmount >= booking.TotalAmount)
        {
            // Đã thu đủ (online hoặc tiền mặt tại quầy) — trả về payment gần nhất thay vì tạo link mới.
            var latestPaidPayment = booking.Payments
                .Where(x => PaymentSupport.IsSettlementPayment(x) && PaymentSupport.IsPaid(x.PaymentStatus))
                .OrderByDescending(x => x.Created)
                .First();
            return PaymentSupport.ToDto(booking, latestPaidPayment);
        }

        var paymentPlan = PaymentSupport.ResolvePaymentPlan(
            booking,
            request.PaymentOption,
            request.DepositPercent,
            paidAmount);
        var orderCode = await PaymentSupport.GeneratePaymentOrderCodeAsync(_context, now, cancellationToken);
        var amount = PaymentSupport.ToPayOsAmount(
            paymentPlan.Amount,
            nameof(paymentPlan.Amount),
            "Số tiền thanh toán phải là số nguyên VND lớn hơn 0.");
        var expiresAt = PaymentSupport.ResolvePaymentExpiresAt(now);
        // Booking thường: link PayOS không được sống quá hạn giữ chỗ (tránh trả tiền cho booking đã nhả ghế).
        var holdLinkExpiry = ResolvePaymentLinkExpiry(booking);
        if (holdLinkExpiry.HasValue && holdLinkExpiry.Value < expiresAt)
        {
            expiresAt = holdLinkExpiry.Value;
        }

        var payment = new Payment
        {
            BookingId = booking.Id,
            PaymentCode = orderCode.ToString(CultureInfo.InvariantCulture),
            Provider = PaymentSupport.PayOsProvider,
            Amount = paymentPlan.Amount,
            Currency = booking.Currency,
            PaymentMethod = PaymentSupport.PayOsProvider,
            PaymentPurpose = paymentPlan.Purpose,
            PaymentStatus = PaymentSupport.PendingStatus,
            ExpiresAt = expiresAt
        };
        booking.Payments.Add(payment);
        _context.Set<Payment>().Add(payment);
        PaymentSupport.ApplyPendingPaymentPlan(booking, paymentPlan, paidAmount);
        PaymentSupport.EnsureCharterPaymentCompletionDeadline(booking, now);
        await _context.SaveChangesAsync(cancellationToken);

        CharterBookingDepositPaymentResult paymentResult;
        try
        {
            paymentResult = await _paymentGateway.CreateDepositPaymentAsync(
                new CharterBookingDepositPaymentRequest(
                    orderCode,
                    amount,
                    PaymentSupport.CreatePaymentDescription(booking),
                    booking.ContactName,
                    booking.ContactEmail,
                    booking.ContactPhone,
                    $"{paymentPlan.Purpose} booking {booking.BookingCode}",
                    expiresAt),
                cancellationToken);
        }
        catch (PaymentGatewayException ex)
        {
            if (await TryRecoverCreatedPaymentAsync(booking, payment, orderCode, amount, cancellationToken))
            {
                return PaymentSupport.ToDto(booking, payment);
            }

            payment.PaymentStatus = PaymentSupport.FailedStatus;
            PaymentSupport.RestorePaymentSummaryFromPaidPayments(booking);
            await _context.SaveChangesAsync(cancellationToken);
            throw new ValidationException([new ValidationFailure("payment", ex.Message)]);
        }

        payment.QrCode = paymentResult.QrCode;
        var wasPaid = PaymentSupport.IsPaid(payment.PaymentStatus);
        PaymentSupport.ApplyPaymentStatus(
            booking,
            payment,
            paymentResult.Status,
            paymentResult.PaymentLinkId,
            paymentResult.CheckoutUrl,
            now);
        await _context.SaveChangesAsync(cancellationToken);
        await PaymentSupport.SendPaymentNotificationIfPaidAsync(
            _context,
            _timeProvider,
            _paymentNotificationSender,
            booking,
            payment,
            wasPaid,
            cancellationToken,
            _bookingTicketPdfRenderer,
            _notificationRealtimeNotifier,
            _charterBookingTicketPdfRenderer);

        return PaymentSupport.ToDto(booking, payment);
    }

    private async Task<PaymentDto> CompleteZeroAmountBookingAsync(
        Booking booking,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var existingPaidPayment = booking.Payments
            .Where(x => PaymentSupport.IsSettlementPayment(x) && PaymentSupport.IsPaid(x.PaymentStatus))
            .OrderByDescending(x => x.Created)
            .FirstOrDefault();
        if (existingPaidPayment is not null)
        {
            return PaymentSupport.ToDto(booking, existingPaidPayment);
        }

        var payment = new Payment
        {
            BookingId = booking.Id,
            PaymentCode = await PaymentSupport.GenerateInternalPaymentCodeAsync(
                _context,
                booking.PointsUsed > 0 ? "POINTS" : "FREE",
                now,
                cancellationToken),
            Provider = PaymentSupport.FreeProvider,
            Amount = 0,
            Currency = booking.Currency,
            PaymentMethod = booking.PointsUsed > 0
                ? PaymentSupport.PointsPaymentMethod
                : PaymentSupport.FreePaymentMethod,
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
        await PaymentSupport.PrepareFullyPaidCharterTicketsAsync(
            _context,
            _timeProvider,
            booking,
            cancellationToken);
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
            _notificationRealtimeNotifier,
            _charterBookingTicketPdfRenderer);

        return PaymentSupport.ToDto(booking, payment);
    }

    /// <summary>
    /// Booking thường: link PayOS chỉ sống tới hạn giữ chỗ để tránh trả tiền cho booking đã nhả ghế.
    /// Charter không giới hạn theo hold.
    /// </summary>
    private static DateTimeOffset? ResolvePaymentLinkExpiry(Booking booking)
    {
        if (Booking.IsCharterBookingType(booking.BookingType) || !booking.HoldExpiresAt.HasValue)
        {
            return null;
        }

        return booking.HoldExpiresAt.Value;
    }

    private async Task<bool> TryRecoverCreatedPaymentAsync(
        Booking booking,
        Payment payment,
        long orderCode,
        long expectedAmount,
        CancellationToken cancellationToken)
    {
        try
        {
            var paymentStatus = await _paymentGateway.GetPaymentAsync(orderCode, cancellationToken);
            if (paymentStatus.OrderCode != orderCode
                || !paymentStatus.Amount.HasValue
                || paymentStatus.Amount.Value != expectedAmount)
            {
                return false;
            }

            var wasPaid = PaymentSupport.IsPaid(payment.PaymentStatus);
            PaymentSupport.ApplyPaymentStatus(
                booking,
                payment,
                paymentStatus.Status,
                paymentStatus.PaymentLinkId,
                paymentStatus.CheckoutUrl,
                _timeProvider.GetUtcNow());
            await _context.SaveChangesAsync(cancellationToken);
            await PaymentSupport.SendPaymentNotificationIfPaidAsync(
                _context,
                _timeProvider,
                _paymentNotificationSender,
                booking,
                payment,
                wasPaid,
                cancellationToken,
                _bookingTicketPdfRenderer,
                _notificationRealtimeNotifier,
                _charterBookingTicketPdfRenderer);
            return true;
        }
        catch (PaymentGatewayException)
        {
            return false;
        }
    }
}

public sealed record SyncPaymentCommand(Guid PaymentId) : IRequest<PaymentDto>;

public sealed record SyncPaymentByOrderCodeCommand(long OrderCode) : IRequest<PaymentDto>;

public sealed class SyncPaymentCommandValidator : AbstractValidator<SyncPaymentCommand>
{
    public SyncPaymentCommandValidator()
    {
        RuleFor(x => x.PaymentId).NotEmpty();
    }
}

public sealed class SyncPaymentByOrderCodeCommandValidator : AbstractValidator<SyncPaymentByOrderCodeCommand>
{
    public SyncPaymentByOrderCodeCommandValidator()
    {
        RuleFor(x => x.OrderCode).GreaterThan(0);
    }
}

public sealed class SyncPaymentCommandHandler :
    IRequestHandler<SyncPaymentCommand, PaymentDto>,
    IRequestHandler<SyncPaymentByOrderCodeCommand, PaymentDto>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;
    private readonly ICharterBookingPaymentGateway _paymentGateway;
    private readonly IPaymentNotificationSender _paymentNotificationSender;
    private readonly IPaymentProcessingLock _paymentProcessingLock;
    private readonly TimeProvider _timeProvider;
    private readonly IBookingTicketPdfRenderer? _bookingTicketPdfRenderer;
    private readonly INotificationRealtimeNotifier _notificationRealtimeNotifier;
    private readonly ICharterBookingTicketPdfRenderer? _charterBookingTicketPdfRenderer;

    public SyncPaymentCommandHandler(
        IApplicationDbContext context,
        IUserContext userContext,
        ICharterBookingPaymentGateway paymentGateway,
        IPaymentNotificationSender paymentNotificationSender,
        TimeProvider timeProvider,
        IPaymentProcessingLock? paymentProcessingLock = null,
        IBookingTicketPdfRenderer? bookingTicketPdfRenderer = null,
        INotificationRealtimeNotifier? notificationRealtimeNotifier = null,
        ICharterBookingTicketPdfRenderer? charterBookingTicketPdfRenderer = null)
    {
        _context = context;
        _userContext = userContext;
        _paymentGateway = paymentGateway;
        _paymentNotificationSender = paymentNotificationSender;
        _paymentProcessingLock = paymentProcessingLock ?? NullPaymentProcessingLock.Instance;
        _timeProvider = timeProvider;
        _bookingTicketPdfRenderer = bookingTicketPdfRenderer;
        _notificationRealtimeNotifier = notificationRealtimeNotifier ?? NullNotificationRealtimeNotifier.Instance;
        _charterBookingTicketPdfRenderer = charterBookingTicketPdfRenderer;
    }

    public async Task<PaymentDto> Handle(SyncPaymentCommand request, CancellationToken cancellationToken)
    {
        var payment = await PaymentSupport.GetOwnedPaymentAsync(
            _context,
            _userContext,
            request.PaymentId,
            includeBookingPayments: true,
            cancellationToken);

        if (!long.TryParse(payment.PaymentCode, NumberStyles.None, CultureInfo.InvariantCulture, out var orderCode))
        {
            throw new ValidationException([new ValidationFailure(nameof(payment.PaymentCode),
                "Mã thanh toán PayOS không hợp lệ.")]);
        }

        await SyncPaymentAsync(payment, orderCode, cancellationToken);

        return PaymentSupport.ToDto(payment.Booking, payment);
    }

    public async Task<PaymentDto> Handle(SyncPaymentByOrderCodeCommand request, CancellationToken cancellationToken)
    {
        var payment = await PaymentSupport.GetOwnedPaymentByOrderCodeAsync(
            _context,
            _userContext,
            request.OrderCode,
            includeBookingPayments: true,
            cancellationToken);

        await SyncPaymentAsync(payment, request.OrderCode, cancellationToken);

        return PaymentSupport.ToDto(payment.Booking, payment);
    }

    private async Task SyncPaymentAsync(Payment payment, long orderCode, CancellationToken cancellationToken)
    {
        await using var paymentLock = await _paymentProcessingLock.TryAcquireAsync(
            payment.PaymentCode,
            cancellationToken);
        if (!paymentLock.Acquired)
        {
            throw new ValidationException([new ValidationFailure("payment",
                "Payment đang được đồng bộ. Vui lòng thử lại sau.")]);
        }

        CharterBookingPaymentStatusResult paymentStatus;
        try
        {
            paymentStatus = await _paymentGateway.GetPaymentAsync(orderCode, cancellationToken);
        }
        catch (PaymentGatewayException ex)
        {
            throw new ValidationException([new ValidationFailure("payment", ex.Message)]);
        }

        var expectedAmount = PaymentSupport.ToPayOsAmount(
            payment.Amount,
            nameof(payment.Amount),
            "Số tiền thanh toán phải là số nguyên VND lớn hơn 0.");
        if (paymentStatus.Amount.HasValue && paymentStatus.Amount.Value != expectedAmount)
        {
            payment.PaymentStatus = PaymentSupport.FailedStatus;
            await _context.SaveChangesAsync(cancellationToken);
            throw new ValidationException([new ValidationFailure("amount",
                "Số tiền thanh toán PayOS không khớp booking.")]);
        }

        var wasPaid = PaymentSupport.IsPaid(payment.PaymentStatus);
        var now = _timeProvider.GetUtcNow();

        // Cùng lý do với webhook: đây là đường thứ hai nhận tin "đã thanh toán" từ PayOS (FE gọi
        // khi khách quay về từ trang thanh toán), nên cũng không được hồi sinh booking chết mù.
        var decision = await LatePaidBookingSupport.EvaluateAsync(
            _context, payment.Booking, now, cancellationToken);

        PaymentSupport.ApplyPaymentStatus(
            payment.Booking,
            payment,
            paymentStatus.Status,
            paymentStatus.PaymentLinkId,
            paymentStatus.CheckoutUrl,
            now,
            applyToBooking: decision.CanConfirm);

        if (!decision.CanConfirm && PaymentSupport.IsPaid(payment.PaymentStatus))
        {
            var refundNotifications = LatePaidBookingSupport.MarkForRefund(
                _context, payment.Booking, payment, decision.BlockReason!, now);
            await _context.SaveChangesAsync(cancellationToken);
            await NotificationSupport.PublishCreatedAsync(
                _notificationRealtimeNotifier, refundNotifications, cancellationToken);
            return;
        }

        await _context.SaveChangesAsync(cancellationToken);
        await PaymentSupport.SendPaymentNotificationIfPaidAsync(
            _context,
            _timeProvider,
            _paymentNotificationSender,
            payment.Booking,
            payment,
            wasPaid,
            cancellationToken,
            _bookingTicketPdfRenderer,
            _notificationRealtimeNotifier,
            _charterBookingTicketPdfRenderer);
    }
}

public sealed record HandlePaymentWebhookCommand(CharterBookingDepositPaymentWebhook Webhook)
    : IRequest<PaymentWebhookResult>;

public sealed class HandlePaymentWebhookCommandHandler
    : IRequestHandler<HandlePaymentWebhookCommand, PaymentWebhookResult>
{
    private readonly IApplicationDbContext _context;
    private readonly ICharterBookingPaymentGateway _paymentGateway;
    private readonly IPaymentNotificationSender _paymentNotificationSender;
    private readonly IPaymentProcessingLock _paymentProcessingLock;
    private readonly TimeProvider _timeProvider;
    private readonly IBookingTicketPdfRenderer? _bookingTicketPdfRenderer;
    private readonly ICharterBookingTicketPdfRenderer? _charterBookingTicketPdfRenderer;
    private readonly INotificationRealtimeNotifier _notificationRealtimeNotifier;

    public HandlePaymentWebhookCommandHandler(
        IApplicationDbContext context,
        ICharterBookingPaymentGateway paymentGateway,
        IPaymentNotificationSender paymentNotificationSender,
        TimeProvider timeProvider,
        IPaymentProcessingLock? paymentProcessingLock = null,
        IBookingTicketPdfRenderer? bookingTicketPdfRenderer = null,
        INotificationRealtimeNotifier? notificationRealtimeNotifier = null,
        ICharterBookingTicketPdfRenderer? charterBookingTicketPdfRenderer = null)
    {
        _context = context;
        _paymentGateway = paymentGateway;
        _paymentNotificationSender = paymentNotificationSender;
        _paymentProcessingLock = paymentProcessingLock ?? NullPaymentProcessingLock.Instance;
        _timeProvider = timeProvider;
        _bookingTicketPdfRenderer = bookingTicketPdfRenderer;
        _notificationRealtimeNotifier = notificationRealtimeNotifier ?? NullNotificationRealtimeNotifier.Instance;
        _charterBookingTicketPdfRenderer = charterBookingTicketPdfRenderer;
    }

    public async Task<PaymentWebhookResult> Handle(
        HandlePaymentWebhookCommand request,
        CancellationToken cancellationToken)
    {
        var webhook = request.Webhook;
        if (!_paymentGateway.IsValidWebhook(webhook))
        {
            throw new ValidationException([new ValidationFailure("signature",
                "Chữ ký webhook PayOS không hợp lệ.")]);
        }

        var paymentCode = webhook.Data.OrderCode.ToString(CultureInfo.InvariantCulture);
        await using var paymentLock = await _paymentProcessingLock.TryAcquireAsync(
            paymentCode,
            cancellationToken);
        if (!paymentLock.Acquired)
        {
            return new PaymentWebhookResult(true, webhook.Data.OrderCode, null, "Payment đang được xử lý.");
        }

        var payment = await _context.Set<Payment>()
            .Include(x => x.Booking)
                .ThenInclude(x => x.Payments)
            .Include(x => x.Booking)
                .ThenInclude(x => x.Passengers)
            .Include(x => x.Booking)
                .ThenInclude(x => x.FromStation)
            .Include(x => x.Booking)
                .ThenInclude(x => x.ToStation)
            .Include(x => x.Booking)
                .ThenInclude(x => x.CharterRoute)
            .Include(x => x.Booking)
                .ThenInclude(x => x.Tickets)
            .SingleOrDefaultAsync(x =>
                x.PaymentCode == paymentCode
                && x.Provider == PaymentSupport.PayOsProvider,
                cancellationToken);

        if (payment is null)
        {
            return new PaymentWebhookResult(false, webhook.Data.OrderCode, null, "Không tìm thấy orderCode trong hệ thống.");
        }

        var expectedAmount = PaymentSupport.ToPayOsAmount(
            payment.Amount,
            nameof(payment.Amount),
            "Số tiền thanh toán phải là số nguyên VND lớn hơn 0.");
        var isPaid = webhook.Success
            && string.Equals(webhook.Code, "00", StringComparison.Ordinal)
            && string.Equals(webhook.Data.Code, "00", StringComparison.Ordinal);

        if (isPaid && webhook.Data.Amount != expectedAmount)
        {
            payment.PaymentStatus = PaymentSupport.FailedStatus;
            await _context.SaveChangesAsync(cancellationToken);
            throw new ValidationException([new ValidationFailure("amount",
                "Số tiền webhook PayOS không khớp booking.")]);
        }

        if (isPaid)
        {
            var wasPaid = PaymentSupport.IsPaid(payment.PaymentStatus);
            var now = _timeProvider.GetUtcNow();

            // Tiền về sau khi booking đã hết hạn giữ chỗ: chỉ hồi sinh nếu ghế còn trống và tàu
            // chưa rời bến khách lên, không thì mở yêu cầu hoàn tiền thay vì phát vé chồng ghế.
            var decision = await LatePaidBookingSupport.EvaluateAsync(
                _context, payment.Booking, now, cancellationToken);

            PaymentSupport.ApplyPaymentStatus(
                payment.Booking,
                payment,
                PaymentSupport.PaidStatus,
                webhook.Data.PaymentLinkId,
                payment.CheckoutUrl,
                now,
                applyToBooking: decision.CanConfirm);

            if (!decision.CanConfirm)
            {
                var blockReason = decision.BlockReason ?? "Booking đã hết hạn giữ chỗ.";
                var refundNotifications = LatePaidBookingSupport.MarkForRefund(
                    _context, payment.Booking, payment, blockReason, now);
                await _context.SaveChangesAsync(cancellationToken);
                await NotificationSupport.PublishCreatedAsync(
                    _notificationRealtimeNotifier, refundNotifications, cancellationToken);
                return new PaymentWebhookResult(
                    true, webhook.Data.OrderCode, payment.PaymentStatus, blockReason);
            }

            await _context.SaveChangesAsync(cancellationToken);
            await PaymentSupport.SendPaymentNotificationIfPaidAsync(
                _context,
                _timeProvider,
                _paymentNotificationSender,
                payment.Booking,
                payment,
                wasPaid,
            cancellationToken,
            _bookingTicketPdfRenderer,
            _notificationRealtimeNotifier,
            _charterBookingTicketPdfRenderer);

            // Charter BH top-up cho hành khách mới → gửi bundle PDF (vé cũ + vé mới) cho khách.
            if (Booking.IsCharterBookingType(payment.Booking.BookingType)
                && string.Equals(payment.PaymentPurpose, "PassengerAddInsurance", StringComparison.OrdinalIgnoreCase)
                && payment.Booking.RemainingAmount <= 0
                && !string.IsNullOrWhiteSpace(payment.Booking.ContactEmail))
            {
                await CharterBookingETicketSupport.SendETicketsIfFullyPaidAsync(
                    _context,
                    _timeProvider,
                    _paymentNotificationSender,
                    payment.Booking,
                    payment,
                    cancellationToken,
                    _charterBookingTicketPdfRenderer);
            }
            return new PaymentWebhookResult(true, webhook.Data.OrderCode, payment.PaymentStatus, "Đã ghi nhận thanh toán.");
        }

        if (!PaymentSupport.IsPaid(payment.PaymentStatus))
        {
            payment.PaymentStatus = PaymentSupport.FailedStatus;
            await _context.SaveChangesAsync(cancellationToken);
        }

        return new PaymentWebhookResult(
            true,
            webhook.Data.OrderCode,
            payment.PaymentStatus,
            "Webhook PayOS không phải trạng thái thanh toán thành công.");
    }
}

public sealed record RequestRefundOtpCommand(Guid PaymentId, string? OtpChannel = null)
    : IRequest<OtpChallengeDto>;

public sealed record GetRefundOtpOptionsQuery(Guid PaymentId)
    : IRequest<RefundOtpOptionsDto>;

public sealed class GetRefundOtpOptionsQueryValidator : AbstractValidator<GetRefundOtpOptionsQuery>
{
    public GetRefundOtpOptionsQueryValidator()
    {
        RuleFor(x => x.PaymentId).NotEmpty();
    }
}

public sealed class GetRefundOtpOptionsQueryHandler
    : IRequestHandler<GetRefundOtpOptionsQuery, RefundOtpOptionsDto>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;
    private readonly IOtpCodeService _otpCodeService;
    private readonly TimeProvider _timeProvider;

    public GetRefundOtpOptionsQueryHandler(
        IApplicationDbContext context,
        IUserContext userContext,
        IOtpCodeService otpCodeService,
        TimeProvider timeProvider)
    {
        _context = context;
        _userContext = userContext;
        _otpCodeService = otpCodeService;
        _timeProvider = timeProvider;
    }

    public async Task<RefundOtpOptionsDto> Handle(
        GetRefundOtpOptionsQuery request,
        CancellationToken cancellationToken)
    {
        var payment = await PaymentSupport.GetOwnedPaymentAsync(
            _context,
            _userContext,
            request.PaymentId,
            includeBookingPayments: true,
            cancellationToken);

        if (!PaymentSupport.IsPaid(payment.PaymentStatus))
        {
            throw new ValidationException([new ValidationFailure(nameof(payment.PaymentStatus),
                "Chỉ có thể yêu cầu OTP hoàn tiền cho payment đã thanh toán.")]);
        }

        var refundAmount = await PaymentSupport.ResolvePolicyRefundAmountAsync(
            _context,
            payment,
            _timeProvider.GetUtcNow(),
            cancellationToken);

        var blockedReason = await RefundOtpSupport.ResolveRequestBlockReasonAsync(
            _context,
            _userContext,
            payment,
            refundAmount,
            cancellationToken);

        var userId = _userContext.UserId
            ?? throw new UnauthorizedAccessException();
        var user = await _context.Set<User>()
            .SingleOrDefaultAsync(x => x.Id == userId, cancellationToken)
            ?? throw new NotFoundException("Current user was not found.");
        AuthSupport.EnsureUserCanLogin(user, requireVerifiedPhone: false);

        var defaultChannel = RefundOtpSupport.ResolveChannel(user, requestedChannel: null);
        var channels = RefundOtpSupport.ResolveAvailableChannels(user, _otpCodeService)
            .Select(x => x with { IsDefault = x.Channel == defaultChannel })
            .ToArray();

        return new RefundOtpOptionsDto(
            payment.Id,
            refundAmount,
            defaultChannel,
            channels,
            RequiresOtp: refundAmount > 0,
            CanRequestOtp: refundAmount > 0 && blockedReason is null,
            CanSubmitRefund: refundAmount <= 0 || blockedReason is null,
            BlockedReason: blockedReason);
    }
}

public sealed record GetPaidPaymentByBookingIdQuery(
    Guid BookingId,
    bool? IsCharterBooking = null)
    : IRequest<Guid>;

public sealed class GetPaidPaymentByBookingIdQueryHandler
    : IRequestHandler<GetPaidPaymentByBookingIdQuery, Guid>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;

    public GetPaidPaymentByBookingIdQueryHandler(
        IApplicationDbContext context,
        IUserContext userContext)
    {
        _context = context;
        _userContext = userContext;
    }

    public async Task<Guid> Handle(GetPaidPaymentByBookingIdQuery request, CancellationToken cancellationToken)
    {
        var userId = _userContext.UserId
            ?? throw new UnauthorizedAccessException();

        var booking = await _context.Set<Booking>()
            .Include(x => x.Payments)
            .FirstOrDefaultAsync(x => x.Id == request.BookingId, cancellationToken)
            ?? throw new NotFoundException("Booking not found.");

        // Customer chỉ refund được booking của mình; staff/admin refund được mọi booking.
        var currentUser = await AuthSupport.TryGetCurrentUserWithRoleAsync(_context, _userContext, cancellationToken);
        var isCustomer = currentUser is not null && AuthSupport.IsCustomer(currentUser);
        if (isCustomer && booking.UserId != userId)
        {
            throw new NotFoundException("Booking not found.");
        }

        // Validate loại booking nếu FE truyền isCharterBooking (optional).
        if (request.IsCharterBooking.HasValue)
        {
            var isCharter = Booking.CharterBookingType.Equals(booking.BookingType, StringComparison.OrdinalIgnoreCase);
            if (request.IsCharterBooking.Value != isCharter)
            {
                throw new ValidationException([new ValidationFailure("isCharterBooking",
                    $"Booking này không phải loại {(request.IsCharterBooking.Value ? "Charter" : "Route")} - không thể refund với isCharterBooking={request.IsCharterBooking.Value}.")]);
            }
        }

        var paidPayments = booking.Payments
            .Where(x => PaymentSupport.IsSettlementPayment(x) && PaymentSupport.IsPaid(x.PaymentStatus));

        // Booking đặt cọc có thể có nhiều payment. Với customer, ưu tiên đúng payment đã được
        // admin mở lại thay vì payment mới nhất để OTP và lệnh refund không lệch payment.
        var paidPayment = (isCustomer
                ? paidPayments
                    .OrderByDescending(x => x.RefundReleasedAt.HasValue && x.CustomerRefundAttempts < 1)
                    .ThenByDescending(x => x.RefundReleasedAt)
                    .ThenByDescending(x => x.Created)
                : paidPayments.OrderByDescending(x => x.Created))
            .FirstOrDefault()
            ?? throw new ValidationException([new ValidationFailure("bookingId",
                "Booking chưa có payment đã thanh toán. Không thể hoàn tiền.")]);

        return paidPayment.Id;
    }
}

public sealed class RequestRefundOtpCommandValidator : AbstractValidator<RequestRefundOtpCommand>
{
    public RequestRefundOtpCommandValidator()
    {
        RuleFor(x => x.PaymentId).NotEmpty();
        RuleFor(x => x.OtpChannel)
            .MaximumLength(20)
            .When(x => x.OtpChannel is not null);
    }
}

public sealed class RequestRefundOtpCommandHandler
    : IRequestHandler<RequestRefundOtpCommand, OtpChallengeDto>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;
    private readonly ISecretHasher _secretHasher;
    private readonly IOtpCodeService _otpCodeService;
    private readonly IOtpSender _otpSender;
    private readonly ISmsOtpSender _smsOtpSender;
    private readonly IOtpPolicy _otpPolicy;
    private readonly IOtpCache _otpCache;
    private readonly TimeProvider _timeProvider;

    public RequestRefundOtpCommandHandler(
        IApplicationDbContext context,
        IUserContext userContext,
        ISecretHasher secretHasher,
        IOtpCodeService otpCodeService,
        IOtpSender otpSender,
        ISmsOtpSender smsOtpSender,
        IOtpPolicy otpPolicy,
        TimeProvider timeProvider,
        IOtpCache? otpCache = null)
    {
        _context = context;
        _userContext = userContext;
        _secretHasher = secretHasher;
        _otpCodeService = otpCodeService;
        _otpSender = otpSender;
        _smsOtpSender = smsOtpSender;
        _otpPolicy = otpPolicy;
        _timeProvider = timeProvider;
        _otpCache = otpCache ?? NullOtpCache.Instance;
    }

    public async Task<OtpChallengeDto> Handle(RequestRefundOtpCommand request, CancellationToken cancellationToken)
    {
        var payment = await PaymentSupport.GetOwnedPaymentAsync(
            _context,
            _userContext,
            request.PaymentId,
            includeBookingPayments: true,
            cancellationToken);

        if (!PaymentSupport.IsPaid(payment.PaymentStatus))
        {
            throw new ValidationException([new ValidationFailure(nameof(payment.PaymentStatus),
                "Chỉ có thể yêu cầu OTP hoàn tiền cho payment đã thanh toán.")]);
        }

        var now = _timeProvider.GetUtcNow();
        var refundAmount = await PaymentSupport.ResolvePolicyRefundAmountAsync(
            _context,
            payment,
            now,
            cancellationToken);

        await RefundOtpSupport.EnsureCanRequestAsync(
            _context,
            _userContext,
            payment,
            refundAmount,
            cancellationToken);

        var userId = _userContext.UserId
            ?? throw new UnauthorizedAccessException();
        var user = await _context.Set<User>()
            .SingleOrDefaultAsync(x => x.Id == userId, cancellationToken)
            ?? throw new NotFoundException("Current user was not found.");
        AuthSupport.EnsureUserCanLogin(user, requireVerifiedPhone: false);

        var otpChannel = RefundOtpSupport.ResolveChannel(user, request.OtpChannel);
        var destination = RefundOtpSupport.ResolveDestination(user, otpChannel);
        var normalizedDestination = otpChannel == OtpChannel.Phone
            ? PhoneRules.ToInternationalFormat(destination)
            : destination.Trim();

        var challengeResult = await _context.ExecuteInTransactionAsync(async ct =>
        {
            await AuthSupport.RetirePendingOtpChallengesAsync(
                _context,
                user.Id,
                OtpPurpose.Refund,
                now,
                ct);

            var otpCode = _otpCodeService.GenerateCode();
            var challenge = new OtpChallenge
            {
                UserId = user.Id,
                Purpose = OtpPurpose.Refund,
                Channel = otpChannel,
                Email = normalizedDestination,
                PendingPhoneNumber = PaymentSupport.CreateRefundOtpPaymentKey(payment.Id),
                CodeHash = _secretHasher.Hash(otpCode),
                ExpiresAt = now.AddMinutes(_otpPolicy.ExpirationMinutes),
                ResendAvailableAt = now.AddSeconds(_otpPolicy.ResendSeconds),
                MaxAttempts = _otpPolicy.MaxAttempts
            };

            _context.Set<OtpChallenge>().Add(challenge);
            await _context.SaveChangesAsync(ct);
            await _otpCache.StoreAsync(challenge, challenge.CodeHash, ct);

            return (
                Id: challenge.Id,
                Destination: normalizedDestination,
                Code: otpCode,
                ExpiresAt: challenge.ExpiresAt,
                ResendAvailableAt: challenge.ResendAvailableAt);
        }, cancellationToken);

        if (otpChannel == OtpChannel.Email)
        {
            await _otpSender.SendAsync(
                challengeResult.Destination,
                challengeResult.Code,
                OtpPurpose.Refund,
                user.FullName,
                cancellationToken);
        }
        else
        {
            await _smsOtpSender.SendAsync(
                challengeResult.Destination,
                challengeResult.Code,
                OtpPurpose.Refund,
                user.FullName,
                cancellationToken);
        }

        return new OtpChallengeDto(
            challengeResult.Id,
            otpChannel == OtpChannel.Email
                ? _otpCodeService.MaskEmail(challengeResult.Destination)
                : _otpCodeService.MaskPhone(challengeResult.Destination),
            challengeResult.ExpiresAt,
            challengeResult.ResendAvailableAt)
        {
            Channel = otpChannel
        };
    }

}

internal static class RefundOtpSupport
{
    public static async Task<string?> ResolveRequestBlockReasonAsync(
        IApplicationDbContext context,
        IUserContext userContext,
        Payment payment,
        decimal refundAmount,
        CancellationToken cancellationToken)
    {
        if (refundAmount <= 0)
        {
            return null;
        }

        var currentUser = await AuthSupport.TryGetCurrentUserWithRoleAsync(
            context,
            userContext,
            cancellationToken);
        if (currentUser is null || !AuthSupport.IsCustomer(currentUser))
        {
            return null;
        }

        if (!HasOpenCustomerRefund(payment))
        {
            return "Bạn không có yêu cầu hoàn tiền đang mở. Vui lòng liên hệ admin.";
        }

        return payment.CustomerRefundAttempts >= 1
            ? "Bạn đã sử dụng 1 lần hoàn tiền. Vui lòng liên hệ admin để được hỗ trợ thêm."
            : null;
    }

    private static bool HasOpenCustomerRefund(Payment payment) =>
        string.IsNullOrWhiteSpace(payment.RefundStatus)
        && (payment.Booking.BookingStatus == BookingStatus.Cancelled
            || payment.RefundReleasedAt.HasValue);

    public static async Task EnsureCanRequestAsync(
        IApplicationDbContext context,
        IUserContext userContext,
        Payment payment,
        decimal refundAmount,
        CancellationToken cancellationToken)
    {
        var blockedReason = await ResolveRequestBlockReasonAsync(
            context,
            userContext,
            payment,
            refundAmount,
            cancellationToken);
        if (blockedReason is not null)
        {
            throw new ValidationException([new ValidationFailure("refund", blockedReason)]);
        }
    }

    public static OtpChannel ResolveChannel(User user, string? requestedChannel)
    {
        var defaultChannel = !string.IsNullOrWhiteSpace(user.PhoneNumber)
            && user.PhoneVerifiedAt.HasValue
            && PhoneRules.IsVietnamPhone(user.PhoneNumber)
                ? OtpChannel.Phone
                : OtpChannel.Email;

        return AuthSupport.ResolveOtpChannel(
            requestedChannel,
            defaultChannel,
            "otpChannel");
    }

    public static string ResolveDestination(User user, OtpChannel otpChannel)
    {
        if (otpChannel == OtpChannel.Email)
        {
            if (string.IsNullOrWhiteSpace(user.Email))
            {
                throw AuthSupport.CreateValidationException("otpChannel",
                    "Tài khoản chưa có email để nhận OTP hoàn tiền.");
            }

            return user.Email;
        }

        if (string.IsNullOrWhiteSpace(user.PhoneNumber)
            || !user.PhoneVerifiedAt.HasValue
            || !PhoneRules.IsVietnamPhone(user.PhoneNumber))
        {
            throw AuthSupport.CreateValidationException("otpChannel",
                "Tài khoản chưa có số điện thoại Việt Nam đã xác thực để nhận OTP hoàn tiền.");
        }

        return user.PhoneNumber;
    }

    public static IReadOnlyList<RefundOtpChannelOptionDto> ResolveAvailableChannels(
        User user,
        IOtpCodeService otpCodeService)
    {
        var channels = new List<RefundOtpChannelOptionDto>();

        if (!string.IsNullOrWhiteSpace(user.Email))
        {
            channels.Add(new RefundOtpChannelOptionDto(
                OtpChannel.Email,
                otpCodeService.MaskEmail(user.Email),
                IsDefault: false));
        }

        if (!string.IsNullOrWhiteSpace(user.PhoneNumber)
            && user.PhoneVerifiedAt.HasValue
            && PhoneRules.IsVietnamPhone(user.PhoneNumber))
        {
            channels.Add(new RefundOtpChannelOptionDto(
                OtpChannel.Phone,
                otpCodeService.MaskPhone(PhoneRules.ToInternationalFormat(user.PhoneNumber)),
                IsDefault: false));
        }

        if (channels.Count == 0)
        {
            throw AuthSupport.CreateValidationException("otpChannel",
                "Tài khoản chưa có email hoặc số điện thoại Việt Nam đã xác thực để nhận OTP hoàn tiền.");
        }

        return channels;
    }
}

public sealed record RefundPaymentCommand(
    Guid PaymentId,
    string Reason,
    string BankBin,
    string AccountNumber,
    string AccountName,
    Guid OtpChallengeId,
    string OtpCode)
    : IRequest<PaymentDto>;

public sealed class RefundPaymentCommandValidator : AbstractValidator<RefundPaymentCommand>
{
    public RefundPaymentCommandValidator()
    {
        RuleFor(x => x.PaymentId).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(100);

        // Thông tin ngân hàng + OTP chỉ bắt buộc khi refundAmount > 0.
        // Khi policy cho refund 0đ (hủy dưới 24 giờ trước giờ khởi hành), handler sẽ đóng sổ booking
        // mà không cần OTP + gọi PayOS → bỏ qua validation cho các field này.
        // Lưu ý: vẫn validate format nếu user nhập (tránh nhập sai format).
        When(x => !string.IsNullOrWhiteSpace(x.BankBin), () =>
        {
            RuleFor(x => x.BankBin).Length(6).Matches("^[0-9]{6}$");
        });
        When(x => !string.IsNullOrWhiteSpace(x.AccountNumber), () =>
        {
            RuleFor(x => x.AccountNumber).MaximumLength(50).Matches("^[0-9]+$");
        });
        When(x => !string.IsNullOrWhiteSpace(x.AccountName), () =>
        {
            RuleFor(x => x.AccountName).MaximumLength(100);
        });
        When(x => !string.IsNullOrWhiteSpace(x.OtpCode), () =>
        {
            RuleFor(x => x.OtpCode).Length(4, 10);
        });
    }
}

public sealed class RefundPaymentCommandHandler : IRequestHandler<RefundPaymentCommand, PaymentDto>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;
    private readonly ICharterBookingPaymentGateway _paymentGateway;
    private readonly ISecretHasher _secretHasher;
    private readonly IOtpCache _otpCache;
    private readonly TimeProvider _timeProvider;

    public RefundPaymentCommandHandler(
        IApplicationDbContext context,
        IUserContext userContext,
        ICharterBookingPaymentGateway paymentGateway,
        ISecretHasher secretHasher,
        TimeProvider timeProvider,
        IOtpCache? otpCache = null)
    {
        _context = context;
        _userContext = userContext;
        _paymentGateway = paymentGateway;
        _secretHasher = secretHasher;
        _timeProvider = timeProvider;
        _otpCache = otpCache ?? NullOtpCache.Instance;
    }

    public async Task<PaymentDto> Handle(RefundPaymentCommand request, CancellationToken cancellationToken)
    {
        var payment = await PaymentSupport.GetOwnedPaymentAsync(
            _context,
            _userContext,
            request.PaymentId,
            includeBookingPayments: true,
            cancellationToken);

        if (!PaymentSupport.IsPaid(payment.PaymentStatus))
        {
            if (payment.RefundStatus == PaymentSupport.RefundedStatus)
            {
                throw new ValidationException([new ValidationFailure(nameof(payment.PaymentStatus),
                    "Payment này đã được hoàn tiền trước đó.")]);
            }
            throw new ValidationException([new ValidationFailure(nameof(payment.PaymentStatus),
                "Chỉ có thể hoàn tiền cho payment đã thanh toán.")]);
        }

        var now = _timeProvider.GetUtcNow();
        var refundAmount = await PaymentSupport.ResolvePolicyRefundAmountAsync(
            _context,
            payment,
            now,
            cancellationToken);

        // Khi chính sách cho refund 0đ (huỷ dưới 24 giờ trước giờ khởi hành): bỏ qua OTP + PayOS + admin-release,
        // customer được quyền đóng sổ booking trực tiếp.
        if (refundAmount <= 0)
        {
            await CloseBookingWithZeroRefundAsync(payment, request, now, cancellationToken);
            return PaymentSupport.ToDto(payment.Booking, payment);
        }

        // Quy tắc "1 lần duy nhất cho customer":
        // - Charter đã Cancelled tự mở lần refund đầu tiên.
        // - Sau một attempt thất bại, admin phải mở lại qua RefundReleasedAt và reset counter.
        // - Admin/Manager/Staff luôn refund được (không giới hạn).
        // - Lưu ý: chỉ áp dụng khi refundAmount > 0; refund 0đ (đóng sổ) không cần admin release.
        var currentUser = await AuthSupport.TryGetCurrentUserWithRoleAsync(_context, _userContext, cancellationToken);
        var isCustomer = currentUser is not null && AuthSupport.IsCustomer(currentUser);
        await RefundOtpSupport.EnsureCanRequestAsync(
            _context,
            _userContext,
            payment,
            refundAmount,
            cancellationToken);

        await VerifyRefundOtpAsync(payment, request.OtpChallengeId, request.OtpCode, now, cancellationToken);

        var referenceId = PaymentSupport.CreateRefundReference(payment, now);
        payment.RefundStatus = PaymentSupport.RefundPendingStatus;
        payment.RefundRequestedAmount = refundAmount;
        payment.RefundMethod = PaymentSupport.PayOsProvider;
        payment.RefundReason = request.Reason.Trim();
        payment.RefundReferenceId = referenceId;
        payment.RefundFailureReason = null;
        payment.RefundProcessedByUserId = _userContext.UserId;

        // Đánh dấu customer đã dùng lượt refund (kể cả khi fail — chính sách "1 lần duy nhất").
        // Admin/Manager/Staff: cũng tăng counter (nếu đã release) nhưng không bị chặn vì isCustomer=false.
        if (isCustomer)
        {
            payment.CustomerRefundAttempts += 1;
        }

        await _context.SaveChangesAsync(cancellationToken);

        CharterBookingRefundPayoutResult refundResult;
        try
        {
            refundResult = await _paymentGateway.CreateRefundPayoutAsync(
                new CharterBookingRefundPayoutRequest(
                    referenceId,
                    PaymentSupport.ToPayOsAmount(refundAmount, "refundAmount", "Số tiền hoàn phải là số nguyên VND lớn hơn 0."),
                    request.Reason.Trim(),
                    request.BankBin.Trim(),
                    request.AccountNumber.Trim(),
                    request.AccountName.Trim(),
                    referenceId),
                cancellationToken);
        }
        catch (PaymentGatewayException ex)
        {
            payment.RefundStatus = PaymentSupport.RefundFailedStatus;
            payment.RefundFailureReason = ex.Message;
            await _context.SaveChangesAsync(cancellationToken);
            throw new ValidationException([new ValidationFailure("refund", ex.Message)]);
        }

        payment.RefundAmount += refundAmount;
        payment.RefundPayoutId = refundResult.PayoutId;
        payment.RefundReferenceId = refundResult.ReferenceId ?? referenceId;
        payment.RefundStatus = refundResult.Status;
        payment.RefundFailureReason = refundResult.Description;
        payment.RefundedAt = now;

        PaymentSupport.ApplyRefundStatus(payment.Booking);
        await PointSupport.ApplyRefundPointAdjustmentsAsync(_context, payment.Booking, now, cancellationToken);
        await PaymentSupport.CancelCharterTripsIfRefundedAsync(_context, payment.Booking, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);

        return PaymentSupport.ToDto(payment.Booking, payment);
    }

    private async Task CloseBookingWithZeroRefundAsync(
        Payment payment,
        RefundPaymentCommand request,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var referenceId = PaymentSupport.CreateRefundReference(payment, now);
        payment.RefundStatus = PaymentSupport.RefundedStatus;
        payment.RefundRequestedAmount = 0m;
        payment.RefundAmount = 0m;
        payment.RefundMethod = PaymentSupport.ManualRefundMethod;
        payment.RefundReason = string.IsNullOrWhiteSpace(request.Reason)
            ? "Đóng sổ booking theo chính sách (refund 0đ - hủy dưới 24 giờ trước giờ khởi hành)."
            : request.Reason.Trim();
        payment.RefundReferenceId = referenceId;
        payment.RefundPayoutId = null;
        payment.RefundFailureReason = null;
        payment.RefundProcessedByUserId = _userContext.UserId;
        payment.RefundedAt = now;

        PaymentSupport.ApplyRefundStatus(payment.Booking);
        await PointSupport.ApplyRefundPointAdjustmentsAsync(_context, payment.Booking, now, cancellationToken);
        await PaymentSupport.CancelCharterTripsIfRefundedAsync(_context, payment.Booking, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
    }

    private async Task VerifyRefundOtpAsync(
        Payment payment,
        Guid challengeId,
        string code,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var userId = _userContext.UserId
            ?? throw new UnauthorizedAccessException();

        var challenge = await _context.Set<OtpChallenge>()
            .SingleOrDefaultAsync(
                x => x.Id == challengeId
                  && x.Purpose == OtpPurpose.Refund
                  && x.UserId == userId,
                cancellationToken)
            ?? throw AuthSupport.CreateValidationException(nameof(challengeId),
                "Không tìm thấy yêu cầu xác thực OTP hoàn tiền.");

        challenge = await AuthSupport.ResolveLatestPendingOtpChallengeAsync(
            _context,
            challenge,
            OtpPurpose.Refund,
            cancellationToken);

        if (!string.Equals(
                challenge.PendingPhoneNumber,
                PaymentSupport.CreateRefundOtpPaymentKey(payment.Id),
                StringComparison.Ordinal))
        {
            throw AuthSupport.CreateValidationException(nameof(challengeId),
                "OTP hoàn tiền không khớp payment cần hoàn.");
        }

        if (challenge.ConsumedAt.HasValue)
        {
            throw AuthSupport.CreateValidationException(nameof(code), "OTP đã được sử dụng.");
        }

        if (challenge.ExpiresAt <= now)
        {
            challenge.ConsumedAt = now;
            await _otpCache.RemoveAsync(challenge.Id, cancellationToken);
            await _context.SaveChangesAsync(cancellationToken);
            throw AuthSupport.CreateValidationException(nameof(code), "OTP đã hết hạn, vui lòng yêu cầu OTP hoàn tiền lại.");
        }

        var codeHash = await _otpCache.GetCodeHashAsync(challenge.Id, cancellationToken) ?? challenge.CodeHash;
        if (!_secretHasher.Verify(code, codeHash))
        {
            challenge.AttemptCount += 1;

            if (challenge.AttemptCount >= challenge.MaxAttempts)
            {
                challenge.ConsumedAt = now;
                await _otpCache.RemoveAsync(challenge.Id, cancellationToken);
            }

            await _context.SaveChangesAsync(cancellationToken);
            throw AuthSupport.CreateValidationException(nameof(code), "OTP không hợp lệ.");
        }

        challenge.AttemptCount += 1;
        challenge.ConsumedAt = now;
        await _otpCache.RemoveAsync(challenge.Id, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
    }
}

public sealed record ManualRefundPaymentCommand(
    Guid PaymentId,
    string Reason,
    string? ReferenceId = null,
    string? PayoutId = null,
    DateTimeOffset? RefundedAt = null)
    : IRequest<PaymentDto>;

public sealed class ManualRefundPaymentCommandValidator : AbstractValidator<ManualRefundPaymentCommand>
{
    public ManualRefundPaymentCommandValidator()
    {
        RuleFor(x => x.PaymentId).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
        RuleFor(x => x.ReferenceId).MaximumLength(100).When(x => x.ReferenceId is not null);
        RuleFor(x => x.PayoutId).MaximumLength(100).When(x => x.PayoutId is not null);
    }
}

public sealed class ManualRefundPaymentCommandHandler : IRequestHandler<ManualRefundPaymentCommand, PaymentDto>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;
    private readonly TimeProvider _timeProvider;

    public ManualRefundPaymentCommandHandler(
        IApplicationDbContext context,
        IUserContext userContext,
        TimeProvider timeProvider)
    {
        _context = context;
        _userContext = userContext;
        _timeProvider = timeProvider;
    }

    public async Task<PaymentDto> Handle(ManualRefundPaymentCommand request, CancellationToken cancellationToken)
    {
        await AuthSupport.EnsureCurrentUserIsAdminAsync(_context, _userContext, cancellationToken);

        var payment = await PaymentSupport.GetPaymentForAdminAsync(
            _context,
            request.PaymentId,
            includeBookingPayments: true,
            cancellationToken);

        if (!PaymentSupport.IsPaid(payment.PaymentStatus))
        {
            throw new ValidationException([new ValidationFailure(nameof(payment.PaymentStatus),
                "Chỉ có thể ghi nhận hoàn tiền cho payment đã thanh toán.")]);
        }

        PaymentSupport.EnsureHasFailedPayOsRefundEvidence(payment);

        var now = _timeProvider.GetUtcNow();
        var refundAmount = await PaymentSupport.ResolveManualRefundAmountAsync(
            _context,
            payment,
            now,
            cancellationToken);
        var referenceId = string.IsNullOrWhiteSpace(request.ReferenceId)
            ? PaymentSupport.CreateManualRefundReference(payment, now)
            : request.ReferenceId.Trim();

        payment.RefundAmount += refundAmount;
        payment.RefundRequestedAmount = refundAmount;
        payment.RefundMethod = PaymentSupport.ManualRefundMethod;
        payment.RefundReason = request.Reason.Trim();
        payment.RefundReferenceId = referenceId;
        payment.RefundPayoutId = string.IsNullOrWhiteSpace(request.PayoutId) ? null : request.PayoutId.Trim();
        payment.RefundStatus = PaymentSupport.RefundedStatus;
        payment.RefundFailureReason = null;
        payment.RefundProcessedByUserId = _userContext.UserId;
        payment.RefundedAt = request.RefundedAt ?? now;

        PaymentSupport.ApplyRefundStatus(payment.Booking);
        await PointSupport.ApplyRefundPointAdjustmentsAsync(_context, payment.Booking, now, cancellationToken);
        await PaymentSupport.CancelCharterTripsIfRefundedAsync(_context, payment.Booking, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);

        return PaymentSupport.ToDto(payment.Booking, payment);
    }
}

internal static class PaymentSupport
{
    public const string PayOsProvider = "PayOS";

    /// <summary>Payment thu tiền mặt tại quầy — không đi qua cổng thanh toán.</summary>
    public const string CounterProvider = "Counter";
    /// <summary>Payment 0đ — hoàn tất nội bộ, không đi qua PayOS.</summary>
    public const string FreeProvider = "System";
    public const string CashPaymentMethod = "Cash";
    public const string FreePaymentMethod = "Free";
    public const string PointsPaymentMethod = "Points";
    public const string PendingStatus = "Pending";
    public const string PaidStatus = "Paid";
    public const string CancelledStatus = "Cancelled";
    public const string FailedStatus = "Failed";
    public const string ExpiredStatus = "Expired";
    public const string RefundedStatus = "Refunded";
    public const string ManualRefundMethod = "Manual";

    public static decimal ResolveRefundPercent(TimeSpan timeUntilDeparture) =>
        timeUntilDeparture >= TimeSpan.FromDays(3) ? 1.0m
        : timeUntilDeparture >= TimeSpan.FromHours(24) ? 0.7m
        : 0m;

    /// <summary>
    /// Thời điểm khởi hành của charter booking (giờ VN, UTC+7).
    /// Trả về null nếu booking không có DepartureDate.
    /// </summary>
    public static DateTimeOffset? ResolveCharterDepartureTime(Booking booking)
    {
        if (!booking.DepartureDate.HasValue)
        {
            return null;
        }

        var startTime = booking.StartTime ?? new TimeOnly(7, 0);
        return new DateTimeOffset(
            booking.DepartureDate.Value.ToDateTime(startTime),
            TimeSpan.FromHours(7));
    }

    /// <summary>
    /// Chính sách hoàn tiền theo thời điểm khởi hành (charter).
    /// Trả về 0 nếu không xác định được thời gian departure.
    /// </summary>
    public static decimal ResolveCharterRefundPercent(Booking booking, DateTimeOffset now)
    {
        var departure = ResolveCharterDepartureTime(booking);
        if (!departure.HasValue)
        {
            return 0m;
        }

        var timeUntilDeparture = departure.Value - now;
        return timeUntilDeparture < TimeSpan.Zero ? 0m : ResolveRefundPercent(timeUntilDeparture);
    }

    public static async Task<decimal> ResolvePolicyRefundAmountAsync(
        IApplicationDbContext context,
        Payment payment,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await EnsureSeatBookingRouteIsRefundableAsync(context, payment.Booking, cancellationToken);

        var departure = await ResolveDepartureAsync(context, payment.Booking, cancellationToken);
        var timeUntilDeparture = departure.HasValue ? departure.Value - now : TimeSpan.MaxValue;
        var refundPercent = ResolveRefundPercent(timeUntilDeparture);

        // Tính tổng tất cả payment đã thanh toán của booking (bao gồm payment gốc và thêm người)
        var bookingPayments = payment.Booking.Payments;
        var totalPaidAmount = bookingPayments
            .Where(x => IsSettlementPayment(x) && IsPaid(x.PaymentStatus))
            .Sum(x => x.Amount);

        // Tổng đã hoàn (tất cả các payment trong booking đã refund)
        var totalRefundedAmount = bookingPayments
            .Sum(x => x.RefundAmount);

        // Số tiền có thể hoàn = Tổng đã thanh toán - Tổng đã hoàn
        var refundableAmount = totalPaidAmount - totalRefundedAmount;

        // Tính refund = min(số tiền có thể hoàn, tổng thanh toán * %).
        // Khi policyPercent = 0% (huỷ dưới 24 giờ trước giờ khởi hành), BE vẫn cho phép
        // refund 0đ để đóng sổ booking → trạng thái Refunded.
        var refundAmount = Math.Min(Math.Floor(totalPaidAmount * refundPercent), refundableAmount);

        return Math.Max(refundAmount, 0m);
    }

    public static async Task<decimal> ResolveManualRefundAmountAsync(
        IApplicationDbContext context,
        Payment payment,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var requestedAmount = payment.RefundRequestedAmount.GetValueOrDefault();
        if (requestedAmount > 0)
        {
            // Tính tổng refundable của cả booking (không chỉ payment đang request)
            var bookingPayments = payment.Booking.Payments;
            var totalPaidAmount = bookingPayments
                .Where(x => IsSettlementPayment(x) && IsPaid(x.PaymentStatus))
                .Sum(x => x.Amount);
            var totalRefundedAmount = bookingPayments.Sum(x => x.RefundAmount);
            var refundableAmount = totalPaidAmount - totalRefundedAmount;

            var refundAmount = Math.Min(requestedAmount, refundableAmount);
            if (refundAmount <= 0)
            {
                throw new ValidationException([new ValidationFailure("refund",
                    "Không còn số tiền hợp lệ để ghi nhận hoàn thủ công.")]);
            }

            return refundAmount;
        }

        return await ResolvePolicyRefundAmountAsync(context, payment, now, cancellationToken);
    }

    public static void EnsureHasFailedPayOsRefundEvidence(Payment payment)
    {
        if (payment.RefundRequestedAmount.GetValueOrDefault() <= 0
            || !string.Equals(payment.RefundMethod, PayOsProvider, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(payment.RefundStatus, RefundFailedStatus, StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(payment.RefundReferenceId)
            || string.IsNullOrWhiteSpace(payment.RefundFailureReason))
        {
            throw new ValidationException([new ValidationFailure("refund",
                "Chỉ được ghi nhận hoàn tiền thủ công sau khi hệ thống đã thử hoàn qua PayOS và lưu trạng thái lỗi.")]);
        }
    }

    /// <summary>Booking thường và sightseeing không hỗ trợ hoàn tiền; policy refund chỉ còn áp cho charter.</summary>
    private static Task EnsureSeatBookingRouteIsRefundableAsync(
        IApplicationDbContext context,
        Booking booking,
        CancellationToken cancellationToken)
    {
        if (Booking.IsCharterBookingType(booking.BookingType))
        {
            return Task.CompletedTask;
        }

        throw new ValidationException([new ValidationFailure("refund",
            "Booking thường và sightseeing không hỗ trợ hoàn tiền.")]);
    }

    private static async Task<DateTimeOffset?> ResolveDepartureAsync(
        IApplicationDbContext context,
        Booking booking,
        CancellationToken cancellationToken)
    {
        if (Booking.IsCharterBookingType(booking.BookingType))
        {
            var startTime = booking.StartTime ?? new TimeOnly(7, 0);
            return new DateTimeOffset(booking.DepartureDate.GetValueOrDefault().ToDateTime(startTime), TimeSpan.FromHours(7));
        }

        // Booking khứ hồi: chính sách hoàn tiền/hạn thanh toán tính theo chiều khởi hành sớm nhất.
        var legTripIds = new List<Guid>();
        if (booking.TripId.HasValue) legTripIds.Add(booking.TripId.Value);
        if (booking.ReturnTripId.HasValue) legTripIds.Add(booking.ReturnTripId.Value);

        if (legTripIds.Count > 0)
        {
            return await context.Set<Trip>()
                .Where(t => legTripIds.Contains(t.Id))
                .Select(t => (DateTimeOffset?)t.DepartureTime)
                .MinAsync(cancellationToken);
        }

        return null;
    }

    public const string UnpaidBookingPaymentStatus = BookingPaymentStatusExtensions.UnpaidValue;
    public const string DepositPaidBookingPaymentStatus = BookingPaymentStatusExtensions.DepositPaidValue;
    public const string PaidBookingPaymentStatus = BookingPaymentStatusExtensions.PaidValue;
    public const string RefundedBookingPaymentStatus = BookingPaymentStatusExtensions.RefundedValue;
    public const string FailedBookingPaymentStatus = BookingPaymentStatusExtensions.FailedValue;
    public const string DepositPurpose = "Deposit";
    public const string FullPurpose = "Full";
    public const string RemainingPurpose = "Remaining";
    public const string RefundPendingStatus = "Pending";
    public const string RefundFailedStatus = "Failed";
    private const decimal DefaultDepositPercent = 50m;

    public static async Task<Booking> GetOwnedBookingAsync(
        IApplicationDbContext context,
        IUserContext userContext,
        Guid bookingId,
        bool includePayments,
        CancellationToken cancellationToken)
    {
        var userId = userContext.UserId
            ?? throw new ValidationException([new ValidationFailure("userId", "User must be authenticated.")]);

        IQueryable<Booking> query = context.Set<Booking>();
        if (includePayments)
        {
            query = query.Include(x => x.Payments);
        }

        query = IncludeCharterBookingNotificationDetails(query);

        var booking = await query.SingleOrDefaultAsync(x => x.Id == bookingId, cancellationToken)
            ?? throw new NotFoundException("Booking not found.");
        if (!CanCurrentUserActOnBooking(booking, userId))
        {
            throw new NotFoundException("Booking not found.");
        }

        return booking;
    }

    public static async Task<Payment> GetOwnedPaymentAsync(
        IApplicationDbContext context,
        IUserContext userContext,
        Guid paymentId,
        bool includeBookingPayments,
        CancellationToken cancellationToken)
    {
        var userId = userContext.UserId
            ?? throw new ValidationException([new ValidationFailure("userId", "User must be authenticated.")]);

        IQueryable<Payment> query = context.Set<Payment>().Include(x => x.Booking);
        if (includeBookingPayments)
        {
            query = query.Include(x => x.Booking).ThenInclude(x => x.Payments);
        }

        query = query
            .Include(x => x.Booking.Boat)
            .Include(x => x.Booking.FromStation)
            .Include(x => x.Booking.ToStation)
            // Payment sync is also used by charter bookings after the PayOS redirect.
            // The e-ticket flow needs the approved passenger manifest immediately.
            .Include(x => x.Booking.Passengers)
            .Include(x => x.Booking.Tickets)
            .Include(x => x.Booking.CharterRoute)
            .Include(x => x.Booking.ItineraryStops)
                .ThenInclude(x => x.Station);

        var payment = await query.SingleOrDefaultAsync(x => x.Id == paymentId, cancellationToken)
            ?? throw new NotFoundException("Payment not found.");
        if (!CanCurrentUserActOnBooking(payment.Booking, userId))
        {
            throw new NotFoundException("Payment not found.");
        }

        return payment;
    }

    public static async Task<Payment> GetPaymentForAdminAsync(
        IApplicationDbContext context,
        Guid paymentId,
        bool includeBookingPayments,
        CancellationToken cancellationToken)
    {
        IQueryable<Payment> query = context.Set<Payment>().Include(x => x.Booking);
        if (includeBookingPayments)
        {
            query = query.Include(x => x.Booking).ThenInclude(x => x.Payments);
        }

        query = query
            .Include(x => x.Booking.Boat)
            .Include(x => x.Booking.FromStation)
            .Include(x => x.Booking.ToStation)
            .Include(x => x.Booking.Passengers)
            .Include(x => x.Booking.Tickets)
            .Include(x => x.Booking.CharterRoute)
            .Include(x => x.Booking.ItineraryStops)
                .ThenInclude(x => x.Station);

        return await query.SingleOrDefaultAsync(x => x.Id == paymentId, cancellationToken)
            ?? throw new NotFoundException("Payment not found.");
    }

    public static async Task<Payment> GetOwnedPaymentByOrderCodeAsync(
        IApplicationDbContext context,
        IUserContext userContext,
        long orderCode,
        bool includeBookingPayments,
        CancellationToken cancellationToken)
    {
        var userId = userContext.UserId
            ?? throw new ValidationException([new ValidationFailure("userId", "User must be authenticated.")]);
        var paymentCode = orderCode.ToString(CultureInfo.InvariantCulture);

        IQueryable<Payment> query = context.Set<Payment>().Include(x => x.Booking);
        if (includeBookingPayments)
        {
            query = query.Include(x => x.Booking).ThenInclude(x => x.Payments);
        }

        query = query
            .Include(x => x.Booking.Boat)
            .Include(x => x.Booking.FromStation)
            .Include(x => x.Booking.ToStation)
            .Include(x => x.Booking.Passengers)
            .Include(x => x.Booking.Tickets)
            .Include(x => x.Booking.CharterRoute)
            .Include(x => x.Booking.ItineraryStops)
                .ThenInclude(x => x.Station);

        var payment = await query.SingleOrDefaultAsync(x =>
                x.PaymentCode == paymentCode
                && x.Provider == PayOsProvider,
                cancellationToken)
            ?? throw new NotFoundException("Payment not found.");
        if (!CanCurrentUserActOnBooking(payment.Booking, userId))
        {
            throw new NotFoundException("Payment not found.");
        }

        return payment;
    }

    /// <summary>
    /// Chủ booking thao tác được trên booking của mình; booking bán tại quầy (khách vãng lai không có
    /// tài khoản) thì staff đã bán vé là người thao tác — nếu không sẽ không ai tạo/đồng bộ được thanh toán.
    /// </summary>
    private static bool CanCurrentUserActOnBooking(Booking booking, Guid userId) =>
        booking.UserId == userId || booking.SoldByStaffId == userId;

    public static void EnsureCanCreatePayment(Booking booking, DateTimeOffset now)
    {
        if (booking.BookingStatus == BookingStatus.Cancelled)
        {
            throw new ValidationException([new ValidationFailure(nameof(booking.BookingStatus),
                "Không thể thanh toán booking đã hủy.")]);
        }

        if (Booking.IsCharterBookingType(booking.BookingType) && booking.BookingStatus == BookingStatus.PendingQuote)
        {
            throw new ValidationException([new ValidationFailure(nameof(booking.BookingStatus),
                "Charter booking chưa được admin nhập tàu và chốt giá.")]);
        }

        if (Booking.IsCharterBookingType(booking.BookingType)
            && (booking.BookingStatus == BookingStatus.Quoted || booking.BookingStatus == BookingStatus.PendingPayment)
            && booking.HoldExpiresAt.HasValue
            && booking.HoldExpiresAt.Value <= now)
        {
            throw new ValidationException([new ValidationFailure(nameof(booking.HoldExpiresAt),
                "Thời hạn phản hồi hoặc thanh toán charter booking đã hết. Vui lòng tạo yêu cầu thuê tàu mới.")]);
        }

        // Charter booking: chặn tạo payment khi sắp khởi hành dưới 24 giờ (trùng với policy 0% refund).
        // Tránh tình trạng khách vẫn thanh toán nhưng không thể hoàn nếu hủy.
        if (Booking.IsCharterBookingType(booking.BookingType)
            && booking.BookingStatus == BookingStatus.PendingPayment)
        {
            var departure = ResolveCharterDepartureTime(booking);
            if (departure.HasValue)
            {
                var timeUntilDeparture = departure.Value - now;
                if (timeUntilDeparture < TimeSpan.Zero)
                {
                    throw new ValidationException([new ValidationFailure(nameof(booking.StartTime),
                        "Chuyến đã khởi hành. Không thể tạo thanh toán cho charter booking quá giờ khởi hành.")]);
                }

                if (timeUntilDeparture < TimeSpan.FromHours(24))
                {
                    throw new ValidationException([new ValidationFailure(nameof(booking.StartTime),
                        $"Đã quá thời gian tối thiểu để thanh toán charter booking (24 giờ trước giờ khởi hành). " +
                        $"Chuyến đi {booking.DepartureDate:dd/MM/yyyy} {booking.StartTime:HH\\:mm} còn {(int)Math.Floor(timeUntilDeparture.TotalHours)} giờ. " +
                        $"Theo chính sách, hủy trong vòng 24 giờ trước giờ khởi hành sẽ không được hoàn tiền — vui lòng liên hệ CSKH để được hỗ trợ.")]);
                }
            }
        }

        if (booking.BookingStatus is BookingStatus.Completed or BookingStatus.Cancelled or BookingStatus.Expired)
        {
            throw new ValidationException([new ValidationFailure(nameof(booking.BookingStatus),
                "Không thể tạo thanh toán cho booking đã hoàn tất, đã hoàn tiền hoặc đã hết hạn giữ chỗ.")]);
        }

        if (booking.TotalAmount < 0
            || (booking.TotalAmount == 0 && Booking.IsCharterBookingType(booking.BookingType)))
        {
            throw new ValidationException([new ValidationFailure(nameof(booking.TotalAmount),
                "Booking chưa có số tiền cần thanh toán.")]);
        }
    }

    public static bool IsFreeRegularBooking(Booking booking) =>
        !Booking.IsCharterBookingType(booking.BookingType) && booking.TotalAmount == 0;

    public static async Task ApplyPromotionForCheckoutAsync(
        IApplicationDbContext context,
        IUserContext userContext,
        Booking booking,
        string? promotionCode,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (promotionCode is null)
        {
            return;
        }

        var normalizedCode = PromotionSupport.NormalizeCode(promotionCode);
        var hasLockedPayment = booking.Payments.Any(x =>
            IsSettlementPayment(x) && (IsPending(x.PaymentStatus) || IsPaid(x.PaymentStatus)));
        if (hasLockedPayment && string.IsNullOrWhiteSpace(normalizedCode))
        {
            throw new ValidationException([new ValidationFailure(nameof(promotionCode),
                "Không thể đổi mã giảm giá khi booking đã có payment đang chờ hoặc đã thanh toán.")]);
        }

        if (string.IsNullOrWhiteSpace(normalizedCode))
        {
            await ReplaceBookingPromotionAsync(context, booking, promotion: null, discount: 0, cancellationToken);
            return;
        }

        var promotion = await context.Set<Promotion>()
            .SingleOrDefaultAsync(p => p.PromotionCode == normalizedCode, cancellationToken)
            ?? throw new ValidationException([new ValidationFailure(nameof(promotionCode),
                "Không tìm thấy mã khuyến mãi.")]);

        if (booking.PromotionId == promotion.Id)
        {
            return;
        }

        if (hasLockedPayment)
        {
            throw new ValidationException([new ValidationFailure(nameof(promotionCode),
                "Không thể đổi mã giảm giá khi booking đã có payment đang chờ hoặc đã thanh toán.")]);
        }

        var userId = userContext.UserId
            ?? throw new ValidationException([new ValidationFailure("userId", "User must be authenticated.")]);

        var discount = await PromotionEligibilitySupport.EnsureAndCalculateAsync(
            context,
            promotion,
            userId,
            booking.SubtotalAmount,
            now,
            nameof(promotionCode),
            new PromotionApplyContext(booking.BookingType),
            booking.Id,
            cancellationToken);

        await ReplaceBookingPromotionAsync(context, booking, promotion, discount, cancellationToken);
    }

    private static async Task ReplaceBookingPromotionAsync(
        IApplicationDbContext context,
        Booking booking,
        Promotion? promotion,
        decimal discount,
        CancellationToken cancellationToken)
    {
        if (booking.PromotionId == promotion?.Id)
        {
            return;
        }

        // Lượt khuyến mãi suy ra từ bookings — gán/xóa PromotionId là tự phản ánh, không cần bookkeeping counter.
        booking.PromotionId = promotion?.Id;
        booking.Promotion = promotion;
        booking.DiscountAmount = promotion is null ? 0 : discount;
        RecalculateUnpaidTotals(booking);

        await context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Áp dụng số point khách muốn dùng ngay tại màn checkout (chạy SAU khi áp promotion).
    /// PointsToUse: null = giữ nguyên (nhưng vẫn kẹp lại nếu bill giảm do đổi promotion),
    /// 0 = bỏ dùng point, N = dùng đúng N point. Balance bị trừ/hoàn ngay tại đây để
    /// tránh double-spend qua nhiều booking song song; booking chết sẽ được hoàn lại.
    /// </summary>
    public static async Task ApplyPointsForCheckoutAsync(
        IApplicationDbContext context,
        Booking booking,
        int? pointsToUse,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var billAmount = booking.SubtotalAmount - booking.DiscountAmount;
        var hasLockedPayment = booking.Payments.Any(x =>
            IsSettlementPayment(x) && (IsPending(x.PaymentStatus) || IsPaid(x.PaymentStatus)));

        // Load user sớm để dùng cho cả validation và tính maxRedeemable theo balance thực.
        var user = await context.Set<User>()
            .Include(u => u.Role)
            .SingleOrDefaultAsync(u => booking.UserId.HasValue && u.Id == booking.UserId.Value, cancellationToken);

        var currentBalance = user?.PointBalance ?? 0;
        // Khi đổi mức sử dụng tại checkout, user đang nắm booking.PointsUsed "tạm giữ" cho đơn này
        // (chưa commit trừ balance); tính balance khả dụng = balance + booking.PointsUsed để cap đúng.
        var availableBalance = currentBalance + booking.PointsUsed;

        var maxRedeemable = PointSupport.CalculateMaxRedeemablePoints(billAmount, availableBalance);
        var targetPoints = pointsToUse ?? Math.Min(booking.PointsUsed, maxRedeemable);

        if (targetPoints == booking.PointsUsed)
        {
            // Quote/promotion có thể vừa ghi đè TotalAmount mà không biết tới điểm — đồng bộ lại nếu lệch.
            var expectedTotal = PriceRoundingSupport.RoundFare(
                booking.SubtotalAmount - booking.DiscountAmount - booking.PointsUsed);
            if (booking.TotalAmount != expectedTotal && !hasLockedPayment)
            {
                RecalculateUnpaidTotals(booking);
                await context.SaveChangesAsync(cancellationToken);
            }

            return;
        }
        if (hasLockedPayment)
        {
            throw new ValidationException([new ValidationFailure("pointsToUse",
                "Không thể đổi số điểm sử dụng khi booking đã có payment đang chờ hoặc đã thanh toán.")]);
        }

        if (!booking.UserId.HasValue)
        {
            throw new ValidationException([new ValidationFailure("pointsToUse",
                "Chỉ tài khoản đăng nhập mới dùng được điểm tích lũy.")]);
        }

        if (user is null)
        {
            throw new ValidationException([new ValidationFailure("pointsToUse",
                "Không tìm thấy tài khoản để trừ điểm.")]);
        }

        if (targetPoints > 0 && !AuthSupport.IsCustomer(user))
        {
            throw new ValidationException([new ValidationFailure("pointsToUse",
                "Chỉ tài khoản khách hàng mới được dùng điểm tích lũy.")]);
        }

        if (targetPoints > maxRedeemable)
        {
            throw new ValidationException([new ValidationFailure("pointsToUse",
                $"Điểm được dùng tối đa bằng giá trị còn phải trả của đơn và số dư hiện có ({maxRedeemable} điểm cho đơn này).")]);
        }

        // Điểm đang giữ trên booking này sẽ được hoàn trước khi trừ theo số mới.
        if (targetPoints > user.PointBalance + booking.PointsUsed)
        {
            throw new ValidationException([new ValidationFailure("pointsToUse",
                $"Số dư điểm không đủ (hiện có {user.PointBalance + booking.PointsUsed} điểm khả dụng cho đơn này).")]);
        }

        if (booking.PointsUsed > 0)
        {
            PointSupport.AddTransaction(
                context,
                user,
                booking.Id,
                PointTransactionTypes.RedeemCancelled,
                booking.PointsUsed,
                $"Hoàn điểm do thay đổi mức sử dụng tại checkout booking {booking.BookingCode}",
                now);
        }

        if (targetPoints > 0)
        {
            PointSupport.AddTransaction(
                context,
                user,
                booking.Id,
                PointTransactionTypes.Redeem,
                -targetPoints,
                $"Dùng điểm thanh toán booking {booking.BookingCode}",
                now);
        }

        booking.PointsUsed = targetPoints;
        RecalculateUnpaidTotals(booking);

        await context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Tính lại tổng phải trả khi booking chưa có payment khóa: Total = Subtotal - Discount - PointsUsed.</summary>
    private static void RecalculateUnpaidTotals(Booking booking)
    {
        booking.TotalAmount = PriceRoundingSupport.RoundFare(
            booking.SubtotalAmount - booking.DiscountAmount - booking.PointsUsed);
        booking.DepositAmount = 0;
        booking.RemainingAmount = booking.TotalAmount;
        booking.PaymentStatus = UnpaidBookingPaymentStatus;
    }

    public static PaymentPlan ResolvePaymentPlan(
        Booking booking,
        BookingPaymentOption paymentOption,
        decimal? requestedDepositPercent,
        decimal paidAmount)
    {
        var outstandingAmount = booking.TotalAmount - paidAmount;
        if (outstandingAmount <= 0)
        {
            throw new ValidationException([new ValidationFailure("payment", "Booking này đã thanh toán đủ.")]);
        }

        if (!Booking.IsCharterBookingType(booking.BookingType))
        {
            if (paymentOption is BookingPaymentOption.Deposit or BookingPaymentOption.Remaining)
            {
                throw new ValidationException([new ValidationFailure(nameof(paymentOption),
                    "Booking thường chỉ hỗ trợ thanh toán 100%.")]);
            }

            return new PaymentPlan(FullPurpose, outstandingAmount, 0, 0);
        }

        if (paymentOption == BookingPaymentOption.Full)
        {
            return paidAmount <= 0
                ? new PaymentPlan(FullPurpose, booking.TotalAmount, booking.TotalAmount, 0)
                : new PaymentPlan(RemainingPurpose, outstandingAmount, booking.DepositAmount, 0);
        }

        if (paymentOption == BookingPaymentOption.Remaining)
        {
            if (paidAmount <= 0)
            {
                throw new ValidationException([new ValidationFailure(nameof(paymentOption),
                    "Booking chưa thanh toán đặt cọc nên chưa có phần còn lại để thanh toán.")]);
            }

            return new PaymentPlan(RemainingPurpose, outstandingAmount, booking.DepositAmount, 0);
        }

        if (paidAmount > 0)
        {
            throw new ValidationException([new ValidationFailure(nameof(paymentOption),
                "Booking đã có thanh toán đặt cọc. Chọn Remaining để thanh toán phần còn lại.")]);
        }

        var depositPercent = requestedDepositPercent ?? DefaultDepositPercent;
        if (depositPercent <= 0 || depositPercent >= 100)
        {
            throw new ValidationException([new ValidationFailure(nameof(requestedDepositPercent),
                "Phần trăm đặt cọc phải lớn hơn 0 và nhỏ hơn 100.")]);
        }

        var depositAmount = decimal.Round(booking.TotalAmount * depositPercent / 100m, 0, MidpointRounding.AwayFromZero);
        if (depositAmount <= 0 || depositAmount >= booking.TotalAmount)
        {
            throw new ValidationException([new ValidationFailure("depositPercent",
                "Số tiền đặt cọc phải lớn hơn 0 và nhỏ hơn tổng tiền booking.")]);
        }

        return new PaymentPlan(DepositPurpose, depositAmount, depositAmount, booking.TotalAmount - depositAmount);
    }

    public static async Task<long> GeneratePaymentOrderCodeAsync(
        IApplicationDbContext context,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var baseCode = now.ToUnixTimeMilliseconds() % 1_000_000_000_000L;
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var orderCode = (baseCode * 100) + attempt;
            var paymentCode = orderCode.ToString(CultureInfo.InvariantCulture);
            if (!await context.Set<Payment>().AnyAsync(x => x.PaymentCode == paymentCode, cancellationToken))
            {
                return orderCode;
            }
        }

        throw new ValidationException([new ValidationFailure("payment",
            "Không thể tạo mã thanh toán duy nhất. Vui lòng thử lại.")]);
    }

    public static async Task<string> GenerateInternalPaymentCodeAsync(
        IApplicationDbContext context,
        string prefix,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var baseCode = $"{prefix}{now.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture)}";
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var paymentCode = $"{baseCode}{attempt:D2}";
            if (!await context.Set<Payment>().AnyAsync(x => x.PaymentCode == paymentCode, cancellationToken))
            {
                return paymentCode;
            }
        }

        throw new ValidationException([new ValidationFailure("payment",
            "Không thể tạo mã thanh toán duy nhất. Vui lòng thử lại.")]);
    }

    public static long ToPayOsAmount(decimal amount, string propertyName, string errorMessage)
    {
        if (amount <= 0 || decimal.Truncate(amount) != amount || amount > long.MaxValue)
        {
            throw new ValidationException([new ValidationFailure(propertyName, errorMessage)]);
        }

        return (long)amount;
    }

    public static string CreatePaymentDescription(Booking booking) =>
        $"{booking.BookingCode[^Math.Min(10, booking.BookingCode.Length)..]}";

    public static string CreateRefundReference(Payment payment, DateTimeOffset now) =>
        $"RF{now.ToUnixTimeMilliseconds()}{payment.Id.ToString("N")[..8]}";

    public static string CreateRefundOtpPaymentKey(Guid paymentId) =>
        paymentId.ToString("N");

    public static string CreateManualRefundReference(Payment payment, DateTimeOffset now) =>
        $"MRF{now.ToUnixTimeMilliseconds()}{payment.Id.ToString("N")[..8]}";

    public static bool IsPayOsPayment(Payment payment) =>
        string.Equals(payment.Provider, PayOsProvider, StringComparison.OrdinalIgnoreCase);

    public static bool IsCounterPayment(Payment payment) =>
        string.Equals(payment.Provider, CounterProvider, StringComparison.OrdinalIgnoreCase);

    public static bool IsFreePayment(Payment payment) =>
        string.Equals(payment.Provider, FreeProvider, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Payment được tính vào số tiền đã thu của booking: PayOS (online), thu tiền mặt tại quầy và đơn 0đ.
    /// Dùng cho mọi phép cộng tiền/hoàn tiền; các chỗ chỉ liên quan tới cổng PayOS (đồng bộ trạng thái,
    /// hết hạn link thanh toán) vẫn lọc riêng bằng <see cref="IsPayOsPayment"/>.
    /// </summary>
    public static bool IsSettlementPayment(Payment payment) =>
        IsPayOsPayment(payment) || IsCounterPayment(payment) || IsFreePayment(payment);

    public static bool IsPending(string status) =>
        string.Equals(status, PendingStatus, StringComparison.OrdinalIgnoreCase);

    public static bool IsPaid(string status) =>
        string.Equals(status, PaidStatus, StringComparison.OrdinalIgnoreCase);

    public static bool IsRefundableForRefund(Payment payment) =>
        string.Equals(payment.PaymentStatus, PaidStatus, StringComparison.OrdinalIgnoreCase)
        && !string.Equals(payment.RefundStatus, RefundedStatus, StringComparison.OrdinalIgnoreCase);

    public static bool IsExpired(Payment payment, DateTimeOffset now)
    {
        var expiresAt = ResolvePaymentExpiresAt(payment);
        return expiresAt.HasValue && expiresAt.Value <= now;
    }

    public static DateTimeOffset ResolvePaymentExpiresAt(DateTimeOffset now) =>
        now + BookingExpirationPolicy.PaymentLinkTtl;

    public static DateTimeOffset? ResolvePaymentExpiresAt(Payment payment) =>
        payment.ExpiresAt
        ?? (payment.Created == default
            ? null
            : payment.Created + BookingExpirationPolicy.PaymentLinkTtl);

    public static bool ExpireStalePendingPayments(Booking booking, DateTimeOffset now)
    {
        var changed = false;
        foreach (var payment in booking.Payments.Where(x => IsPayOsPayment(x) && IsPending(x.PaymentStatus) && IsExpired(x, now)))
        {
            payment.PaymentStatus = ExpiredStatus;
            changed = true;
        }

        if (changed)
        {
            RestorePaymentSummaryFromPaidPayments(booking);
        }

        return changed;
    }

    public static void EnsureCharterPaymentCompletionDeadline(Booking booking, DateTimeOffset now)
    {
        if (!Booking.IsCharterBookingType(booking.BookingType)
            || (booking.BookingStatus != BookingStatus.Quoted && booking.BookingStatus != BookingStatus.PendingPayment))
        {
            return;
        }

        var responseDeadline = now + BookingExpirationPolicy.CharterQuoteResponseTtl;
        if (!booking.HoldExpiresAt.HasValue || booking.HoldExpiresAt.Value <= responseDeadline)
        {
            booking.HoldExpiresAt = now + BookingExpirationPolicy.CharterPaymentCompletionTtl;
        }
    }

    public static decimal GetPaidAmount(Booking booking) =>
        booking.Payments
            .Where(x => IsSettlementPayment(x) && IsPaid(x.PaymentStatus))
            .Sum(x => x.Amount);

    public static void RestorePaymentSummaryFromPaidPayments(Booking booking)
    {
        var paidAmount = GetPaidAmount(booking);
        booking.DepositAmount = Math.Min(paidAmount, booking.TotalAmount);
        booking.RemainingAmount = Math.Max(booking.TotalAmount - paidAmount, 0);
        booking.PaymentStatus = paidAmount <= 0
            ? HasAnyFailedSettlementPayment(booking)
                ? FailedBookingPaymentStatus
                : UnpaidBookingPaymentStatus
            : paidAmount >= booking.TotalAmount
                ? PaidBookingPaymentStatus
                : DepositPaidBookingPaymentStatus;
    }

    public static bool HasAnyFailedSettlementPayment(Booking booking) =>
        booking.Payments.Any(x => IsSettlementPayment(x) && IsFailed(x.PaymentStatus));

    public static bool IsFailed(string status) =>
        string.Equals(status, FailedStatus, StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, CancelledStatus, StringComparison.OrdinalIgnoreCase);

    public static void ApplyPendingPaymentPlan(Booking booking, PaymentPlan paymentPlan, decimal paidAmount)
    {
        if (paidAmount <= 0)
        {
            booking.PaymentStatus = UnpaidBookingPaymentStatus;
            booking.DepositAmount = paymentPlan.DepositAmount;
            booking.RemainingAmount = paymentPlan.RemainingAmount;
            return;
        }

        booking.PaymentStatus = DepositPaidBookingPaymentStatus;
        booking.DepositAmount = Math.Min(paidAmount, booking.TotalAmount);
        booking.RemainingAmount = Math.Max(booking.TotalAmount - paidAmount, 0);
    }

    public static string ResolvePaymentStatus(string status)
    {
        if (string.Equals(status, "PAID", StringComparison.OrdinalIgnoreCase)
            || string.Equals(status, PaidStatus, StringComparison.OrdinalIgnoreCase))
        {
            return PaidStatus;
        }

        if (string.Equals(status, "CANCELLED", StringComparison.OrdinalIgnoreCase)
            || string.Equals(status, CancelledStatus, StringComparison.OrdinalIgnoreCase))
        {
            return CancelledStatus;
        }

        if (string.Equals(status, "EXPIRED", StringComparison.OrdinalIgnoreCase)
            || string.Equals(status, ExpiredStatus, StringComparison.OrdinalIgnoreCase))
        {
            return ExpiredStatus;
        }

        if (string.Equals(status, FailedStatus, StringComparison.OrdinalIgnoreCase))
        {
            return FailedStatus;
        }

        return PendingStatus;
    }

    public static void ApplyPaymentStatus(
        Booking booking,
        Payment payment,
        string providerStatus,
        string? paymentLinkId,
        string? checkoutUrl,
        DateTimeOffset now,
        bool applyToBooking = true)
    {
        payment.PaymentStatus = ResolvePaymentStatus(providerStatus);
        payment.ProviderTransactionId ??= paymentLinkId;
        payment.CheckoutUrl ??= checkoutUrl;

        if (!IsPaid(payment.PaymentStatus))
        {
            return;
        }

        payment.PaidAt ??= now;

        // applyToBooking=false: tiền có thật nên payment vẫn ghi Paid, nhưng booking đã chết và
        // không hồi sinh được (ghế mất hoặc tàu đã chạy) — xem LatePaidBookingSupport.
        if (!applyToBooking)
        {
            return;
        }

        var paidAmount = GetPaidAmount(booking);
        if (paidAmount >= booking.TotalAmount)
        {
            booking.PaymentStatus = PaidBookingPaymentStatus;
            booking.DepositAmount = Math.Min(paidAmount, booking.TotalAmount);
            booking.RemainingAmount = 0;
        }
        else
        {
            booking.PaymentStatus = DepositPaidBookingPaymentStatus;
            booking.DepositAmount = Math.Max(booking.DepositAmount, paidAmount);
            booking.RemainingAmount = booking.TotalAmount - paidAmount;
        }

        booking.BookingStatus = BookingStatus.Confirmed;
    }

    public static void ApplyRefundStatus(Booking booking)
    {
        var paidAmount = GetPaidAmount(booking);
        var refundedAmount = booking.Payments
            .Where(IsSettlementPayment)
            .Sum(x => x.RefundAmount);

        // Trường hợp đặc biệt: refund 0đ (huỷ dưới 24 giờ trước giờ khởi hành theo chính sách)
        // → vẫn đóng sổ booking: set PaymentStatus = Refunded, BookingStatus = Cancelled.
        // Điều kiện: payment đã được đánh dấu Refunded (qua refund 0đ) nhưng chưa có tiền hoàn thật.
        var zeroRefundClosed = refundedAmount <= 0
            && booking.Payments.Any(x => IsSettlementPayment(x)
                && string.Equals(x.RefundStatus, RefundedStatus, StringComparison.OrdinalIgnoreCase)
                && x.RefundAmount <= 0);
        if (refundedAmount <= 0 && !zeroRefundClosed)
        {
            return;
        }

        if (refundedAmount >= paidAmount)
        {
            booking.PaymentStatus = RefundedBookingPaymentStatus;
            booking.BookingStatus = BookingStatus.Cancelled;
            foreach (var payment in booking.Payments.Where(x => IsSettlementPayment(x) && IsPaid(x.PaymentStatus)))
            {
                if (payment.RefundAmount >= payment.Amount)
                {
                    payment.PaymentStatus = RefundedStatus;
                }
            }
            return;
        }

        if (zeroRefundClosed)
        {
            // Refund 0đ đóng sổ → booking chuyển sang Refunded (không phải partial refund).
            booking.PaymentStatus = RefundedBookingPaymentStatus;
            booking.BookingStatus = BookingStatus.Cancelled;
            return;
        }

        // Partial refund (refundedAmount < paidAmount): booking đã hủy và có refund 1 phần.
        // Không dùng "PartiallyRefunded" ở booking-level — payment.RefundAmount là nguồn truth.
        // booking.PaymentStatus giữ nguyên giá trị paid-level (Paid/DepositPaid) + set BookingStatus = Cancelled.
        booking.BookingStatus = BookingStatus.Cancelled;
    }

    /// <summary>
    /// Charter booking đã hoàn tiền đủ → hủy các trip đã sinh từ booking đó.
    /// Logic: chỉ cancel trips khi <c>booking.PaymentStatus</c> đã chuyển sang <c>Refunded</c>
    /// (không phải cancel booking kiểu admin/user thường — handler admin đã tự cancel trips ở <c>AdminCharterBookingCommands</c>).
    /// </summary>
    public static async Task CancelCharterTripsIfRefundedAsync(
        IApplicationDbContext context,
        Booking booking,
        CancellationToken cancellationToken)
    {
        if (!Booking.IsCharterBookingType(booking.BookingType)
            || !booking.PaymentStatusEnum.IsRefunded())
        {
            return;
        }

        await CharterBookingTripSupport.CancelLinkedTripsAsync(
            context,
            booking.Id,
            $"Charter booking {booking.BookingCode} đã hoàn tiền đủ — tự động hủy trip liên quan.",
            cancellationToken);
    }

    public static async Task SendPaymentNotificationIfPaidAsync(
        IApplicationDbContext context,
        TimeProvider timeProvider,
        IPaymentNotificationSender paymentNotificationSender,
        Booking booking,
        Payment payment,
        bool wasPaid,
        CancellationToken cancellationToken,
        IBookingTicketPdfRenderer? bookingTicketPdfRenderer = null,
        INotificationRealtimeNotifier? notificationRealtimeNotifier = null,
        ICharterBookingTicketPdfRenderer? charterBookingTicketPdfRenderer = null)
    {
        var isPaid = IsPaid(payment.PaymentStatus);
        IReadOnlyList<Ticket> issuedTickets = [];
        if (isPaid)
        {
            issuedTickets = await TicketIssueSupport.EnsureRegularBookingPassengerTicketsAsync(
                context,
                booking,
                timeProvider,
                cancellationToken);

            // Persist charter QR, seat reservations and tickets before any realtime/email call.
            // External notification failures must never leave a paid charter without tickets.
            var charterTicketResult = await PrepareFullyPaidCharterTicketsAsync(
                context,
                timeProvider,
                booking,
                cancellationToken);
            if (charterTicketResult is not null)
            {
                await context.SaveChangesAsync(cancellationToken);
            }
        }

        if (wasPaid || !isPaid || !payment.PaidAt.HasValue)
        {
            return;
        }

        // KHÔNG tích điểm ở đây: điểm chỉ được cộng sau khi khách dùng xong dịch vụ
        // (PointSupport.AwardCompletionPointsAsync), tránh khách tích điểm → tiêu điểm → hoàn tiền đơn gốc.

        // In-app notification lưu DB trước khi gửi email: email (Brevo) có thể lỗi nhưng thông báo vẫn còn.
        var inAppNotification = NotificationSupport.AddBookingPaymentSucceededNotification(
            context, booking, payment, payment.PaidAt.Value);
        if (inAppNotification is not null)
        {
            await context.SaveChangesAsync(cancellationToken);
            await NotificationSupport.PublishCreatedAsync(
                notificationRealtimeNotifier, [inAppNotification], cancellationToken);
        }

        // Charter booking → báo thêm admin/manager (kèm Manager được giao) để theo dõi.
        if (Booking.IsCharterBookingType(booking.BookingType))
        {
            var isFullyPaid = booking.RemainingAmount <= 0;
            var adminNotifications = await NotificationSupport.AddCharterPaymentReceivedNotificationsAsync(
                context,
                booking,
                payment.Amount,
                isFullyPaid,
                payment.PaidAt.Value,
                cancellationToken);
            if (adminNotifications.Count > 0)
            {
                await context.SaveChangesAsync(cancellationToken);
                await NotificationSupport.PublishCreatedAsync(
                    notificationRealtimeNotifier, adminNotifications, cancellationToken);
            }
        }

        if (!Booking.IsCharterBookingType(booking.BookingType))
        {
            // Booking thường: gửi email vé điện tử (QR chung + QR riêng + PDF) thay cho email xác nhận thanh toán.
            await RegularBookingETicketSupport.SendETicketEmailsAsync(
                context,
                paymentNotificationSender,
                booking,
                payment,
                issuedTickets,
                cancellationToken,
                bookingTicketPdfRenderer);
            return;
        }

        // Charter: luôn gửi email xác nhận thanh toán (cả khi cọc và khi trả đủ).
        await SendCharterPaymentSucceededAsync(
            context,
            paymentNotificationSender,
            booking,
            payment,
            cancellationToken);

        // Charter: nếu trả đủ 100% và đã có danh sách hành khách → gửi email mã vé ngay.
        // Nếu chưa có hành khách thì skip — sẽ được gửi sau khi khách import danh sách.
        var isFullyPaidAfterThisPayment = booking.RemainingAmount <= 0;
        if (isFullyPaidAfterThisPayment && !string.IsNullOrWhiteSpace(booking.ContactEmail))
        {
            await CharterBookingETicketSupport.SendETicketsIfFullyPaidAsync(
                context,
                timeProvider,
                paymentNotificationSender,
                booking,
                payment,
                cancellationToken,
                charterBookingTicketPdfRenderer);
        }
    }

    internal static Task<PassengerTicketEnsureResult?> PrepareFullyPaidCharterTicketsAsync(
        IApplicationDbContext context,
        TimeProvider timeProvider,
        Booking booking,
        CancellationToken cancellationToken)
    {
        if (!Booking.IsCharterBookingType(booking.BookingType)
            || !string.Equals(booking.PaymentStatus, PaidBookingPaymentStatus, StringComparison.OrdinalIgnoreCase)
            || booking.RemainingAmount > 0)
        {
            return Task.FromResult<PassengerTicketEnsureResult?>(null);
        }

        return CharterBookingTicketSupport.EnsurePassengerTicketsAsync(
            context,
            booking,
            timeProvider,
            cancellationToken);
    }

    private static async Task SendCharterPaymentSucceededAsync(
        IApplicationDbContext context,
        IPaymentNotificationSender paymentNotificationSender,
        Booking booking,
        Payment payment,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(booking.ContactEmail))
        {
            return;
        }

        var notification = CreatePaymentSucceededNotification(booking, payment);

        await paymentNotificationSender.SendPaymentSucceededAsync(notification, cancellationToken);
    }

    public static PaymentSucceededNotification CreatePaymentSucceededNotification(
        Booking booking,
        Payment payment)
    {
        if (string.IsNullOrWhiteSpace(booking.ContactEmail))
        {
            throw new InvalidOperationException("Booking contact email is required to create a payment notification.");
        }

        return CreatePaymentSucceededNotification(
            booking,
            payment,
            booking.ContactEmail.Trim(),
            string.IsNullOrWhiteSpace(booking.ContactName) ? "Quy khach" : booking.ContactName.Trim());
    }

    public static PaymentSucceededNotification CreatePaymentSucceededNotification(
        Booking booking,
        Payment payment,
        string recipientEmail,
        string recipientName)
    {
        if (!payment.PaidAt.HasValue)
        {
            throw new InvalidOperationException("Payment paid time is required to create a payment notification.");
        }

        var contactName = string.IsNullOrWhiteSpace(recipientName)
            ? "Quy khach"
            : recipientName.Trim();
        var isFullyPaid = string.Equals(
                booking.PaymentStatus,
                PaidBookingPaymentStatus,
                StringComparison.OrdinalIgnoreCase)
            || booking.RemainingAmount <= 0;
        var isCharterBooking = Booking.IsCharterBookingType(booking.BookingType);
        var stops = isCharterBooking
            ? booking.ItineraryStops
                .OrderBy(x => x.StopOrder)
                .Select(x => new PaymentNotificationStop(
                    x.Station.StationName,
                    x.Note,
                    x.StayDurationMinutes))
                .ToList()
            : [];
        var vessels = isCharterBooking
            ? ResolveNotificationVessels(booking)
            : [];
        var insurance = (booking.InsuranceSnapshots ?? new List<BookingInsuranceSnapshot>()).FirstOrDefault() is { } insuranceSnapshot
            ? new PaymentNotificationInsurance(
                insuranceSnapshot.Name,
                insuranceSnapshot.Quantity,
                insuranceSnapshot.TotalAmount,
                string.IsNullOrWhiteSpace(insuranceSnapshot.Currency)
                    ? booking.Currency
                    : insuranceSnapshot.Currency)
            : null;
        var remainingPaymentDueAt = isCharterBooking && !isFullyPaid && booking.RemainingAmount > 0
            ? booking.HoldExpiresAt
            : null;

        return new PaymentSucceededNotification(
            recipientEmail.Trim(),
            contactName,
            booking.ContactPhone,
            booking.BookingCode,
            isCharterBooking ? Booking.CharterBookingType : "Booking",
            booking.Created == default ? payment.PaidAt.Value : booking.Created,
            payment.PaymentCode,
            payment.PaymentPurpose,
            payment.Amount,
            booking.Currency,
            booking.TotalAmount,
            booking.PaymentStatus,
            booking.DepositAmount,
            booking.RemainingAmount,
            payment.PaidAt.Value,
            isFullyPaid,
            isCharterBooking ? booking.DepartureDate : null,
            isCharterBooking ? booking.StartTime : null,
            isCharterBooking ? booking.RentalUnit?.ToString() : null,
            isCharterBooking ? booking.DurationValue.GetValueOrDefault() : 0,
            isCharterBooking ? booking.PassengerCount.GetValueOrDefault() : booking.Passengers.Count,
            isCharterBooking ? booking.Boat?.Name : null,
            isCharterBooking ? booking.FromStation?.StationName : null,
            ResolveStationAddress(isCharterBooking ? booking.FromStation : null),
            isCharterBooking ? booking.ToStation?.StationName : null,
            ResolveStationAddress(isCharterBooking ? booking.ToStation : null),
            stops,
            vessels,
            insurance,
            remainingPaymentDueAt);
    }

    private static IQueryable<Booking> IncludeCharterBookingNotificationDetails(IQueryable<Booking> query) =>
        query
            .Include(x => x.Boat)
            .Include(x => x.CharterBoats)
                .ThenInclude(x => x.Boat)
            .Include(x => x.Passengers)
            .Include(x => x.Tickets)
                .ThenInclude(x => x.BookingPassenger)
            .Include(x => x.FromStation)
            .Include(x => x.ToStation)
            .Include(x => x.ItineraryStops)
                .ThenInclude(x => x.Station);

    private static IReadOnlyList<PaymentNotificationVessel> ResolveNotificationVessels(Booking booking)
    {
        var vessels = booking.CharterBoats
            .Where(x => x.Boat is not null)
            .OrderBy(x => x.BoatOrder)
            .Select(x => new PaymentNotificationVessel(
                x.Boat.Name,
                x.Boat.SeatCount,
                x.BoatOrder))
            .ToList();

        if (vessels.Count > 0)
        {
            return vessels;
        }

        return booking.Boat is null
            ? []
            : [new PaymentNotificationVessel(booking.Boat.Name, booking.Boat.SeatCount)];
    }

    private static string? ResolveStationAddress(Station? station) =>
        string.IsNullOrWhiteSpace(station?.Address)
            ? station?.StationName
            : station.Address;

    public static PaymentDto ToDto(Booking booking, Payment payment) =>
        new(
            payment.Id,
            booking.Id,
            booking.BookingCode,
            payment.PaymentCode,
            payment.Provider,
            payment.ProviderTransactionId,
            payment.Amount,
            payment.Currency,
            payment.PaymentMethod,
            payment.PaymentPurpose,
            payment.PaymentStatus,
            booking.BookingStatus.ToString(),
            booking.PaymentStatus,
            booking.DepositAmount,
            booking.RemainingAmount,
            payment.CheckoutUrl,
            payment.QrCode,
            payment.PaidAt,
            ResolvePaymentExpiresAt(payment),
            booking.HoldExpiresAt,
            payment.RefundAmount,
            payment.RefundRequestedAmount,
            payment.RefundMethod,
            payment.RefundReason,
            payment.RefundReferenceId,
            payment.RefundPayoutId,
            payment.RefundStatus,
            payment.RefundFailureReason,
            payment.RefundProcessedByUserId,
            payment.RefundedAt,
            payment.CustomerRefundAttempts,
            payment.RefundReleasedAt,
            payment.RefundReleasedByUserId,
            payment.RefundReleasedReason,
            IsRefundable: IsRefundableForRefund(payment));

    public sealed record PaymentPlan(
        string Purpose,
        decimal Amount,
        decimal DepositAmount,
        decimal RemainingAmount);
}

public sealed record CancelPaymentByOrderCodeCommand(long OrderCode) : IRequest<bool>;

public sealed class CancelPaymentByOrderCodeCommandValidator : AbstractValidator<CancelPaymentByOrderCodeCommand>
{
    public CancelPaymentByOrderCodeCommandValidator()
    {
        RuleFor(x => x.OrderCode).GreaterThan(0);
    }
}

public sealed class CancelPaymentByOrderCodeCommandHandler : IRequestHandler<CancelPaymentByOrderCodeCommand, bool>
{
    private readonly IApplicationDbContext _context;
    private readonly IBoatHoldService _boatHoldService;
    private readonly ICharterBookingRealtimeNotifier _realtimeNotifier;
    private readonly TimeProvider _timeProvider;

    public CancelPaymentByOrderCodeCommandHandler(
        IApplicationDbContext context,
        IBoatHoldService? boatHoldService = null,
        ICharterBookingRealtimeNotifier? realtimeNotifier = null,
        TimeProvider? timeProvider = null)
    {
        _context = context;
        _boatHoldService = boatHoldService ?? NullBoatHoldService.Instance;
        _realtimeNotifier = realtimeNotifier ?? NullCharterBookingRealtimeNotifier.Instance;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<bool> Handle(CancelPaymentByOrderCodeCommand request, CancellationToken cancellationToken)
    {
        var paymentCode = request.OrderCode.ToString(CultureInfo.InvariantCulture);

        var payment = await _context.Set<Payment>()
            .Include(x => x.Booking)
                .ThenInclude(x => x.Tickets)
            .Include(x => x.Booking)
                .ThenInclude(x => x.CharterBoats)
            .Include(x => x.Booking)
                .ThenInclude(x => x.Promotion)
            .Include(x => x.Booking)
                .ThenInclude(x => x.Payments)
            .SingleOrDefaultAsync(x =>
                x.PaymentCode == paymentCode
                && x.Provider == PaymentSupport.PayOsProvider,
                cancellationToken);

        if (payment is null)
        {
            return false;
        }

        // Chỉ cancel nếu payment đang Pending
        if (!PaymentSupport.IsPending(payment.PaymentStatus))
        {
            return false;
        }

        payment.PaymentStatus = PaymentSupport.CancelledStatus;
        var booking = payment.Booking;
        var now = _timeProvider.GetUtcNow();

        // Nếu là charter booking đang chờ thanh toán, hủy luôn booking
        if (Booking.IsCharterBookingType(booking.BookingType)
            && (booking.BookingStatus == BookingStatus.Quoted || booking.BookingStatus == BookingStatus.PendingPayment))
        {
            booking.BookingStatus = BookingStatus.Cancelled;
            booking.HoldExpiresAt = null;

            foreach (var ticket in booking.Tickets)
            {
                ticket.TicketStatus = TicketStatus.Cancelled;
            }

            await PointSupport.ReturnRedeemedPointsAsync(
                _context,
                booking,
                $"Hoàn điểm do charter booking {booking.BookingCode} bị hủy khi khách cancel PayOS",
                now,
                cancellationToken);

            await CharterBookingTripSupport.CancelLinkedTripsAsync(
                _context,
                booking.Id,
                $"Charter booking {booking.BookingCode} đã bị hủy khi khách cancel PayOS.",
                cancellationToken);

            await CharterBookingRouteSupport.DeactivateOwnedRouteAsync(
                _context,
                booking,
                cancellationToken);

            await _context.SaveChangesAsync(cancellationToken);

            await _realtimeNotifier.PublishChangedAsync(
                new CharterBookingRealtimeEvent(
                    booking.Id,
                    "Cancelled",
                    booking.BookingStatus.ToString(),
                    booking.PaymentStatus),
                cancellationToken);

            foreach (var boatId in CharterBookingBoatSelectionSupport.ResolveSelectedBoatIds(booking))
            {
                await _boatHoldService.ReleaseAsync(
                    booking.Id,
                    boatId,
                    booking.DepartureDate.GetValueOrDefault(),
                    booking.StartTime,
                    booking.RentalUnit.GetValueOrDefault(),
                    booking.DurationValue.GetValueOrDefault(),
                    cancellationToken);
            }
        }
        else
        {
            await _context.SaveChangesAsync(cancellationToken);
        }

        return true;
    }
}
