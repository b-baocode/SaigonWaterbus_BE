using System.Globalization;
using FluentValidation.Results;
using SaigonWaterbus.Application.Common.Exceptions;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Application.Payments;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.CharterBookings;

public sealed record CreateCharterBookingPaymentCommand(
    Guid BookingId,
    CharterBookingPaymentOption PaymentOption = CharterBookingPaymentOption.Deposit,
    decimal? DepositPercent = null)
    : IRequest<CreateCharterBookingPaymentResult>;

public sealed class CreateCharterBookingPaymentCommandValidator
    : AbstractValidator<CreateCharterBookingPaymentCommand>
{
    public CreateCharterBookingPaymentCommandValidator()
    {
        RuleFor(x => x.BookingId).NotEmpty();
        RuleFor(x => x.PaymentOption).IsInEnum();
        RuleFor(x => x.DepositPercent)
            .GreaterThan(0)
            .LessThan(100)
            .When(x => x.PaymentOption == CharterBookingPaymentOption.Deposit && x.DepositPercent.HasValue)
            .WithMessage("Phần trăm đặt cọc phải lớn hơn 0 và nhỏ hơn 100.");
    }
}

public sealed class CreateCharterBookingPaymentCommandHandler
    : IRequestHandler<CreateCharterBookingPaymentCommand, CreateCharterBookingPaymentResult>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;
    private readonly ICharterBookingPaymentGateway _paymentGateway;
    private readonly IPaymentNotificationSender _paymentNotificationSender;
    private readonly TimeProvider _timeProvider;
    private readonly ICharterBookingRealtimeNotifier _realtimeNotifier;
    private readonly INotificationRealtimeNotifier _notificationRealtimeNotifier;

    public CreateCharterBookingPaymentCommandHandler(
        IApplicationDbContext context,
        IUserContext userContext,
        ICharterBookingPaymentGateway paymentGateway,
        IPaymentNotificationSender paymentNotificationSender,
        TimeProvider timeProvider,
        ICharterBookingRealtimeNotifier? realtimeNotifier = null,
        INotificationRealtimeNotifier? notificationRealtimeNotifier = null)
    {
        _context = context;
        _userContext = userContext;
        _paymentGateway = paymentGateway;
        _paymentNotificationSender = paymentNotificationSender;
        _timeProvider = timeProvider;
        _realtimeNotifier = realtimeNotifier ?? NullCharterBookingRealtimeNotifier.Instance;
        _notificationRealtimeNotifier = notificationRealtimeNotifier ?? NullNotificationRealtimeNotifier.Instance;
    }

    public async Task<CreateCharterBookingPaymentResult> Handle(
        CreateCharterBookingPaymentCommand request,
        CancellationToken cancellationToken)
    {
        var booking = await CharterBookingPaymentSupport.GetOwnedCharterBookingAsync(
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

        CharterBookingPaymentSupport.EnsureCanCreatePayment(booking, now);

        var existingPendingPayment = booking.Payments
            .Where(CharterBookingPaymentSupport.IsPayOsPayment)
            .OrderByDescending(x => x.Created)
            .FirstOrDefault(x =>
                CharterBookingPaymentSupport.IsPending(x.PaymentStatus)
                && !PaymentSupport.IsExpired(x, now)
                && !string.IsNullOrWhiteSpace(x.CheckoutUrl));
        if (existingPendingPayment is not null)
        {
            return CharterBookingPaymentSupport.ToCreatePaymentResult(booking, existingPendingPayment);
        }

        var paidAmount = CharterBookingPaymentSupport.GetPaidAmount(booking);
        if (paidAmount >= booking.TotalAmount)
        {
            var latestPaidPayment = booking.Payments
                .Where(CharterBookingPaymentSupport.IsPayOsPayment)
                .OrderByDescending(x => x.Created)
                .First(x => CharterBookingPaymentSupport.IsPaid(x.PaymentStatus));
            return CharterBookingPaymentSupport.ToCreatePaymentResult(booking, latestPaidPayment);
        }

        var paymentPlan = CharterBookingPaymentSupport.ResolvePaymentPlan(
            booking,
            request.PaymentOption,
            request.DepositPercent,
            paidAmount);
        var orderCode = await CharterBookingPaymentSupport.GeneratePaymentOrderCodeAsync(
            _context,
            now,
            cancellationToken);
        var amount = CharterBookingPaymentSupport.ToPayOsAmount(
            paymentPlan.Amount,
            nameof(paymentPlan.Amount),
            "Tổng tiền thanh toán phải là số nguyên VND lớn hơn 0.");
        var expiresAt = PaymentSupport.ResolvePaymentExpiresAt(now);

        var payment = new Payment
        {
            BookingId = booking.Id,
            PaymentCode = orderCode.ToString(CultureInfo.InvariantCulture),
            Provider = CharterBookingPaymentSupport.PayOsProvider,
            Amount = paymentPlan.Amount,
            Currency = booking.Currency,
            PaymentMethod = CharterBookingPaymentSupport.PayOsProvider,
            PaymentPurpose = paymentPlan.Purpose,
            PaymentStatus = CharterBookingPaymentSupport.PendingStatus,
            ExpiresAt = expiresAt
        };
        booking.Payments.Add(payment);
        _context.Set<Payment>().Add(payment);
        CharterBookingPaymentSupport.ApplyPendingPaymentPlan(booking, paymentPlan, paidAmount);
        PaymentSupport.EnsureCharterPaymentCompletionDeadline(booking, now);
        await _context.SaveChangesAsync(cancellationToken);
        await _realtimeNotifier.PublishChangedAsync(
            new CharterBookingRealtimeEvent(
                booking.Id,
                "PaymentCreated",
                booking.BookingStatus.ToString(),
                booking.PaymentStatus,
                now),
            cancellationToken);

        CharterBookingDepositPaymentResult paymentResult;
        try
        {
            paymentResult = await _paymentGateway.CreateDepositPaymentAsync(
                new CharterBookingDepositPaymentRequest(
                    orderCode,
                    amount,
                    CharterBookingPaymentSupport.CreatePaymentDescription(booking),
                    booking.ContactName,
                    booking.ContactEmail,
                    booking.ContactPhone,
                    $"{paymentPlan.Purpose} charter booking {booking.BookingCode}",
                    expiresAt),
                cancellationToken);
        }
        catch (PaymentGatewayException ex)
        {
            if (await TryRecoverCreatedPaymentAsync(booking, payment, orderCode, amount, cancellationToken))
            {
                return CharterBookingPaymentSupport.ToCreatePaymentResult(booking, payment);
            }

            payment.PaymentStatus = CharterBookingPaymentSupport.FailedStatus;
            CharterBookingPaymentSupport.RestorePaymentSummaryFromPaidPayments(booking);
            await _context.SaveChangesAsync(cancellationToken);
            await _realtimeNotifier.PublishChangedAsync(
                new CharterBookingRealtimeEvent(
                    booking.Id,
                    "PaymentFailed",
                    booking.BookingStatus.ToString(),
                    booking.PaymentStatus,
                    _timeProvider.GetUtcNow()),
                cancellationToken);
            throw new ValidationException([new ValidationFailure("payment", ex.Message)]);
        }

        payment.QrCode = paymentResult.QrCode;
        var wasPaid = CharterBookingPaymentSupport.IsPaid(payment.PaymentStatus);
        CharterBookingPaymentSupport.ApplyPaymentStatus(
            booking,
            payment,
            paymentResult.Status,
            paymentResult.PaymentLinkId,
            paymentResult.CheckoutUrl,
            now);
        await _context.SaveChangesAsync(cancellationToken);
        await _realtimeNotifier.PublishChangedAsync(
            new CharterBookingRealtimeEvent(
                booking.Id,
                "PaymentUpdated",
                booking.BookingStatus.ToString(),
                booking.PaymentStatus,
                _timeProvider.GetUtcNow()),
            cancellationToken);
        await PaymentSupport.SendPaymentNotificationIfPaidAsync(
            _context,
            _timeProvider,
            _paymentNotificationSender,
            booking,
            payment,
            wasPaid,
            cancellationToken,
            notificationRealtimeNotifier: _notificationRealtimeNotifier);

        return CharterBookingPaymentSupport.ToCreatePaymentResult(booking, payment);
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

            var wasPaid = CharterBookingPaymentSupport.IsPaid(payment.PaymentStatus);
            CharterBookingPaymentSupport.ApplyPaymentStatus(
                booking,
                payment,
                paymentStatus.Status,
                paymentStatus.PaymentLinkId,
                paymentStatus.CheckoutUrl,
                _timeProvider.GetUtcNow());
            await _context.SaveChangesAsync(cancellationToken);
            await _realtimeNotifier.PublishChangedAsync(
                new CharterBookingRealtimeEvent(
                    booking.Id,
                    "PaymentRecovered",
                    booking.BookingStatus.ToString(),
                    booking.PaymentStatus,
                    _timeProvider.GetUtcNow()),
                cancellationToken);
            await PaymentSupport.SendPaymentNotificationIfPaidAsync(
                _context,
                _timeProvider,
                _paymentNotificationSender,
                booking,
                payment,
                wasPaid,
                cancellationToken,
                notificationRealtimeNotifier: _notificationRealtimeNotifier);
            return true;
        }
        catch (PaymentGatewayException)
        {
            return false;
        }
    }
}

public sealed record SyncCharterBookingPaymentCommand(Guid BookingId)
    : IRequest<SyncCharterBookingPaymentResult>;

public sealed class SyncCharterBookingPaymentCommandValidator
    : AbstractValidator<SyncCharterBookingPaymentCommand>
{
    public SyncCharterBookingPaymentCommandValidator()
    {
        RuleFor(x => x.BookingId).NotEmpty();
    }
}

public sealed class SyncCharterBookingPaymentCommandHandler
    : IRequestHandler<SyncCharterBookingPaymentCommand, SyncCharterBookingPaymentResult>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;
    private readonly ICharterBookingPaymentGateway _paymentGateway;
    private readonly IPaymentNotificationSender _paymentNotificationSender;
    private readonly TimeProvider _timeProvider;
    private readonly ICharterBookingRealtimeNotifier _realtimeNotifier;

    private readonly INotificationRealtimeNotifier _notificationRealtimeNotifier;

    public SyncCharterBookingPaymentCommandHandler(
        IApplicationDbContext context,
        IUserContext userContext,
        ICharterBookingPaymentGateway paymentGateway,
        IPaymentNotificationSender paymentNotificationSender,
        TimeProvider timeProvider,
        ICharterBookingRealtimeNotifier? realtimeNotifier = null,
        INotificationRealtimeNotifier? notificationRealtimeNotifier = null)
    {
        _context = context;
        _userContext = userContext;
        _paymentGateway = paymentGateway;
        _paymentNotificationSender = paymentNotificationSender;
        _timeProvider = timeProvider;
        _realtimeNotifier = realtimeNotifier ?? NullCharterBookingRealtimeNotifier.Instance;
        _notificationRealtimeNotifier = notificationRealtimeNotifier ?? NullNotificationRealtimeNotifier.Instance;
    }

    public async Task<SyncCharterBookingPaymentResult> Handle(
        SyncCharterBookingPaymentCommand request,
        CancellationToken cancellationToken)
    {
        var booking = await CharterBookingPaymentSupport.GetOwnedCharterBookingAsync(
            _context,
            _userContext,
            request.BookingId,
            includePayments: true,
            cancellationToken);

        var payment = CharterBookingPaymentSupport.GetLatestPayOsPayment(booking)
            ?? throw new NotFoundException("Không tìm thấy thanh toán PayOS cho charter booking.");

        if (!long.TryParse(payment.PaymentCode, NumberStyles.None, CultureInfo.InvariantCulture, out var orderCode))
        {
            throw new ValidationException([new ValidationFailure(nameof(payment.PaymentCode),
                "Mã thanh toán PayOS không hợp lệ.")]);
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

        var expectedAmount = CharterBookingPaymentSupport.ToPayOsAmount(
            payment.Amount,
            nameof(payment.Amount),
            "Số tiền thanh toán phải là số nguyên VND lớn hơn 0.");
        if (paymentStatus.Amount.HasValue && paymentStatus.Amount.Value != expectedAmount)
        {
            payment.PaymentStatus = CharterBookingPaymentSupport.FailedStatus;
            await _context.SaveChangesAsync(cancellationToken);
            throw new ValidationException([new ValidationFailure("amount",
                "Số tiền thanh toán PayOS không khớp booking.")]);
        }

        var wasPaid = CharterBookingPaymentSupport.IsPaid(payment.PaymentStatus);
        CharterBookingPaymentSupport.ApplyPaymentStatus(
            booking,
            payment,
            paymentStatus.Status,
            paymentStatus.PaymentLinkId,
            paymentStatus.CheckoutUrl,
            _timeProvider.GetUtcNow());
        await _context.SaveChangesAsync(cancellationToken);
        await _realtimeNotifier.PublishChangedAsync(
            new CharterBookingRealtimeEvent(
                booking.Id,
                "PaymentSynced",
                booking.BookingStatus.ToString(),
                booking.PaymentStatus,
                _timeProvider.GetUtcNow()),
            cancellationToken);
        await PaymentSupport.SendPaymentNotificationIfPaidAsync(
            _context,
            _timeProvider,
            _paymentNotificationSender,
            booking,
            payment,
            wasPaid,
            cancellationToken,
            notificationRealtimeNotifier: _notificationRealtimeNotifier);

        return CharterBookingPaymentSupport.ToSyncPaymentResult(booking, payment);
    }
}

public sealed record HandleCharterBookingPaymentWebhookCommand(
    CharterBookingDepositPaymentWebhook Webhook)
    : IRequest<CharterBookingPaymentWebhookResult>;

public sealed class HandleCharterBookingPaymentWebhookCommandHandler
    : IRequestHandler<HandleCharterBookingPaymentWebhookCommand, CharterBookingPaymentWebhookResult>
{
    private readonly IApplicationDbContext _context;
    private readonly ICharterBookingPaymentGateway _paymentGateway;
    private readonly IPaymentNotificationSender _paymentNotificationSender;
    private readonly TimeProvider _timeProvider;
    private readonly ICharterBookingRealtimeNotifier _realtimeNotifier;

    private readonly INotificationRealtimeNotifier _notificationRealtimeNotifier;

    public HandleCharterBookingPaymentWebhookCommandHandler(
        IApplicationDbContext context,
        ICharterBookingPaymentGateway paymentGateway,
        IPaymentNotificationSender paymentNotificationSender,
        TimeProvider timeProvider,
        ICharterBookingRealtimeNotifier? realtimeNotifier = null,
        INotificationRealtimeNotifier? notificationRealtimeNotifier = null)
    {
        _context = context;
        _paymentGateway = paymentGateway;
        _paymentNotificationSender = paymentNotificationSender;
        _timeProvider = timeProvider;
        _realtimeNotifier = realtimeNotifier ?? NullCharterBookingRealtimeNotifier.Instance;
        _notificationRealtimeNotifier = notificationRealtimeNotifier ?? NullNotificationRealtimeNotifier.Instance;
    }

    public async Task<CharterBookingPaymentWebhookResult> Handle(
        HandleCharterBookingPaymentWebhookCommand request,
        CancellationToken cancellationToken)
    {
        var webhook = request.Webhook;
        if (!_paymentGateway.IsValidWebhook(webhook))
        {
            throw new ValidationException([new ValidationFailure("signature",
                "Chữ ký webhook PayOS không hợp lệ.")]);
        }

        var paymentCode = webhook.Data.OrderCode.ToString(CultureInfo.InvariantCulture);
        var payment = await _context.Set<Payment>()
            .Include(x => x.Booking)
                .ThenInclude(x => x.Payments)
            .SingleOrDefaultAsync(x =>
                x.PaymentCode == paymentCode
                && x.Provider == CharterBookingPaymentSupport.PayOsProvider,
                cancellationToken);

        if (payment is null)
        {
            return new CharterBookingPaymentWebhookResult(
                false,
                webhook.Data.OrderCode,
                null,
                "Không tìm thấy orderCode trong hệ thống.");
        }

        if (!Booking.IsCharterBookingType(payment.Booking.BookingType))
        {
            return new CharterBookingPaymentWebhookResult(
                false,
                webhook.Data.OrderCode,
                payment.PaymentStatus,
                "OrderCode không thuộc charter booking.");
        }

        var booking = await CharterBookingQuerySupport.BuildBaseQuery(_context)
            .Include(x => x.Boat)
            .Include(x => x.FromStation)
            .Include(x => x.ToStation)
            .Include(x => x.ItineraryStops)
                .ThenInclude(x => x.Station)
            .Include(x => x.Payments)
            .Include(x => x.Passengers)
            .Include(x => x.Tickets)
                .ThenInclude(x => x.BookingPassenger)
            .SingleAsync(x => x.Id == payment.BookingId, cancellationToken);
        payment = booking.Payments.Single(x => x.Id == payment.Id);

        var expectedAmount = CharterBookingPaymentSupport.ToPayOsAmount(
            payment.Amount,
            nameof(payment.Amount),
            "Số tiền thanh toán phải là số nguyên VND lớn hơn 0.");
        var isPaid = webhook.Success
            && string.Equals(webhook.Code, "00", StringComparison.Ordinal)
            && string.Equals(webhook.Data.Code, "00", StringComparison.Ordinal);

        if (isPaid && webhook.Data.Amount != expectedAmount)
        {
            payment.PaymentStatus = CharterBookingPaymentSupport.FailedStatus;
            await _context.SaveChangesAsync(cancellationToken);
            throw new ValidationException([new ValidationFailure("amount",
                "Số tiền webhook PayOS không khớp booking.")]);
        }

        if (isPaid)
        {
            var wasPaid = CharterBookingPaymentSupport.IsPaid(payment.PaymentStatus);
            CharterBookingPaymentSupport.ApplyPaymentStatus(
                booking,
                payment,
                CharterBookingPaymentSupport.PaidStatus,
                webhook.Data.PaymentLinkId,
                payment.CheckoutUrl,
                _timeProvider.GetUtcNow());
            await _context.SaveChangesAsync(cancellationToken);
            await _realtimeNotifier.PublishChangedAsync(
                new CharterBookingRealtimeEvent(
                    booking.Id,
                    "PaymentPaid",
                    booking.BookingStatus.ToString(),
                    booking.PaymentStatus,
                    _timeProvider.GetUtcNow()),
                cancellationToken);
            await PaymentSupport.SendPaymentNotificationIfPaidAsync(
                _context,
                _timeProvider,
                _paymentNotificationSender,
                booking,
                payment,
                wasPaid,
                cancellationToken,
                notificationRealtimeNotifier: _notificationRealtimeNotifier);

            return new CharterBookingPaymentWebhookResult(
                true,
                webhook.Data.OrderCode,
                payment.PaymentStatus,
                "Đã ghi nhận thanh toán charter booking.");
        }

        if (!CharterBookingPaymentSupport.IsPaid(payment.PaymentStatus))
        {
            payment.PaymentStatus = CharterBookingPaymentSupport.FailedStatus;
            await _context.SaveChangesAsync(cancellationToken);
            await _realtimeNotifier.PublishChangedAsync(
                new CharterBookingRealtimeEvent(
                    booking.Id,
                    "PaymentFailed",
                    booking.BookingStatus.ToString(),
                    booking.PaymentStatus,
                    _timeProvider.GetUtcNow()),
                cancellationToken);
        }

        return new CharterBookingPaymentWebhookResult(
            true,
            webhook.Data.OrderCode,
            payment.PaymentStatus,
            "Webhook PayOS không phải trạng thái thanh toán thành công.");
    }
}

internal static class CharterBookingPaymentSupport
{
    public const string PayOsProvider = "PayOS";
    public const string PendingStatus = "Pending";
    public const string PaidStatus = "Paid";
    public const string CancelledStatus = "Cancelled";
    public const string FailedStatus = "Failed";
    public const string ExpiredStatus = "Expired";
    public const string UnpaidBookingPaymentStatus = BookingPaymentStatusExtensions.UnpaidValue;
    public const string DepositPaidBookingPaymentStatus = BookingPaymentStatusExtensions.DepositPaidValue;
    public const string PaidBookingPaymentStatus = BookingPaymentStatusExtensions.PaidValue;
    public const string DepositPurpose = "Deposit";
    public const string FullPurpose = "Full";
    public const string RemainingPurpose = "Remaining";
    /// <summary>% đặt cọc mặc định cho charter booking khi FE không gửi <c>depositPercent</c>.</summary>
    public const decimal DefaultDepositPercent = 50m;

    /// <summary>
    /// BE tính sẵn để FE không phải đoán:
    /// - <c>hasDepositPaid</c> = true nếu đã thu phần cọc thành công (DepositAmount &gt; 0).
    /// - <c>suggestedDepositAmount</c> = 50% TotalAmount khi chưa cọc, = 0 khi đã cọc xong.
    /// </summary>
    public static (bool HasDepositPaid, decimal SuggestedDepositAmount) ComputeDepositPlan(Booking booking)
    {
        var paidAmount = GetPaidAmount(booking);
        var hasDepositPaid = paidAmount > 0 && paidAmount < booking.TotalAmount
            || paidAmount >= booking.TotalAmount;
        var suggestedDepositAmount = hasDepositPaid
            ? 0m
            : decimal.Round(booking.TotalAmount * DefaultDepositPercent / 100m, 0, MidpointRounding.AwayFromZero);
        return (hasDepositPaid, suggestedDepositAmount);
    }

    public static async Task<Booking> GetOwnedCharterBookingAsync(
        IApplicationDbContext context,
        IUserContext userContext,
        Guid bookingId,
        bool includePayments,
        CancellationToken cancellationToken)
    {
        var userId = userContext.UserId
            ?? throw new ValidationException([new ValidationFailure("userId", "User must be authenticated.")]);

        IQueryable<Booking> query = CharterBookingQuerySupport.BuildBaseQuery(context)
            .Include(x => x.Boat)
            .Include(x => x.FromStation)
            .Include(x => x.ToStation)
            .Include(x => x.ItineraryStops)
                .ThenInclude(x => x.Station);
        if (includePayments)
        {
            query = query.Include(x => x.Payments);
        }

        var booking = await query.SingleOrDefaultAsync(x => x.Id == bookingId, cancellationToken)
            ?? throw new NotFoundException("Charter booking not found.");

        if (booking.UserId != userId)
        {
            throw new NotFoundException("Charter booking not found.");
        }

        return booking;
    }

    public static void EnsureCanCreatePayment(Booking booking, DateTimeOffset now)
    {
        if (booking.BookingStatus == BookingStatus.Cancelled)
        {
            throw new ValidationException([new ValidationFailure(nameof(booking.BookingStatus),
                "Không thể thanh toán charter booking đã hủy.")]);
        }

        if (booking.BookingStatus == BookingStatus.PendingQuote)
        {
            throw new ValidationException([new ValidationFailure(nameof(booking.BookingStatus),
                "Charter booking chưa được admin nhập tàu và chốt giá.")]);
        }

        if ((booking.BookingStatus == BookingStatus.Quoted || booking.BookingStatus == BookingStatus.PendingPayment)
            && booking.HoldExpiresAt.HasValue
            && booking.HoldExpiresAt.Value <= now)
        {
            throw new ValidationException([new ValidationFailure(nameof(booking.HoldExpiresAt),
                "Thời hạn phản hồi hoặc thanh toán charter booking đã hết. Vui lòng tạo yêu cầu thuê tàu mới.")]);
        }

        if (booking.BookingStatus is BookingStatus.Completed or BookingStatus.Cancelled)
        {
            throw new ValidationException([new ValidationFailure(nameof(booking.BookingStatus),
                "Không thể tạo thanh toán cho charter booking đã hoàn tất.")]);
        }

        if (booking.TotalAmount <= 0)
        {
            throw new ValidationException([new ValidationFailure(nameof(booking.TotalAmount),
                "Charter booking chưa có số tiền cần thanh toán.")]);
        }
    }

    public static CharterBookingPaymentPlan ResolvePaymentPlan(
        Booking booking,
        CharterBookingPaymentOption paymentOption,
        decimal? requestedDepositPercent,
        decimal paidAmount)
    {
        var outstandingAmount = booking.TotalAmount - paidAmount;
        if (outstandingAmount <= 0)
        {
            throw new ValidationException([new ValidationFailure("payment",
                "Charter booking này đã thanh toán đủ.")]);
        }

        if (paymentOption == CharterBookingPaymentOption.Full)
        {
            return paidAmount <= 0
                ? new CharterBookingPaymentPlan(
                    FullPurpose,
                    booking.TotalAmount,
                    booking.TotalAmount,
                    0)
                : new CharterBookingPaymentPlan(
                    RemainingPurpose,
                    outstandingAmount,
                    booking.DepositAmount,
                    0);
        }

        if (paymentOption == CharterBookingPaymentOption.Remaining)
        {
            if (paidAmount <= 0)
            {
                throw new ValidationException([new ValidationFailure(nameof(paymentOption),
                    "Charter booking chưa thanh toán đặt cọc nên chưa có phần còn lại để thanh toán.")]);
            }

            return new CharterBookingPaymentPlan(
                RemainingPurpose,
                outstandingAmount,
                booking.DepositAmount,
                0);
        }

        if (paidAmount > 0)
        {
            throw new ValidationException([new ValidationFailure("paymentOption",
                "Charter booking đã có thanh toán đặt cọc. Chọn Remaining hoặc Full để thanh toán phần còn lại.")]);
        }

        var depositPercent = requestedDepositPercent ?? DefaultDepositPercent;
        if (depositPercent <= 0 || depositPercent >= 100)
        {
            throw new ValidationException([new ValidationFailure(nameof(requestedDepositPercent),
                "Phần trăm đặt cọc phải lớn hơn 0 và nhỏ hơn 100.")]);
        }

        var depositAmount = decimal.Round(
            booking.TotalAmount * depositPercent / 100m,
            0,
            MidpointRounding.AwayFromZero);
        if (depositAmount <= 0 || depositAmount >= booking.TotalAmount)
        {
            throw new ValidationException([new ValidationFailure("depositPercent",
                "Số tiền đặt cọc phải lớn hơn 0 và nhỏ hơn tổng tiền booking.")]);
        }

        return new CharterBookingPaymentPlan(
            DepositPurpose,
            depositAmount,
            depositAmount,
            booking.TotalAmount - depositAmount);
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

    public static long ToPayOsAmount(decimal amount, string propertyName, string errorMessage)
    {
        if (amount <= 0 || decimal.Truncate(amount) != amount || amount > long.MaxValue)
        {
            throw new ValidationException([new ValidationFailure(propertyName, errorMessage)]);
        }

        return (long)amount;
    }

    public static string CreatePaymentDescription(Booking booking) =>
        $"{booking.DepartureDate.GetValueOrDefault():yyMMdd}{booking.Id.ToString("N")[^6..].ToUpperInvariant()}";

    public static bool IsPayOsPayment(Payment payment) =>
        string.Equals(payment.Provider, PayOsProvider, StringComparison.OrdinalIgnoreCase);

    public static bool IsPending(string status) =>
        string.Equals(status, PendingStatus, StringComparison.OrdinalIgnoreCase);

    public static bool IsPaid(string status) =>
        string.Equals(status, PaidStatus, StringComparison.OrdinalIgnoreCase);

    public static decimal GetPaidAmount(Booking booking) =>
        booking.Payments
            .Where(x => IsPayOsPayment(x) && IsPaid(x.PaymentStatus))
            .Sum(x => x.Amount);

    public static void RestorePaymentSummaryFromPaidPayments(Booking booking)
    {
        var paidAmount = GetPaidAmount(booking);
        booking.DepositAmount = Math.Min(paidAmount, booking.TotalAmount);
        booking.RemainingAmount = Math.Max(booking.TotalAmount - paidAmount, 0);
        booking.PaymentStatus = paidAmount <= 0
            ? UnpaidBookingPaymentStatus
            : paidAmount >= booking.TotalAmount
                ? PaidBookingPaymentStatus
                : DepositPaidBookingPaymentStatus;
    }

    public static void ApplyPendingPaymentPlan(
        Booking booking,
        CharterBookingPaymentPlan paymentPlan,
        decimal paidAmount)
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

    public static Payment? GetLatestPayOsPayment(Booking booking) =>
        booking.Payments
            .Where(IsPayOsPayment)
            .OrderByDescending(x => x.Created)
            .FirstOrDefault();

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
        DateTimeOffset now)
    {
        payment.PaymentStatus = ResolvePaymentStatus(providerStatus);
        payment.ProviderTransactionId ??= paymentLinkId;
        payment.CheckoutUrl ??= checkoutUrl;

        if (IsPaid(payment.PaymentStatus))
        {
            payment.PaidAt ??= now;
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
    }

    public static CreateCharterBookingPaymentResult ToCreatePaymentResult(
        Booking booking,
        Payment payment) =>
        new(
            booking.Id,
            payment.Id,
            payment.PaymentCode,
            payment.Amount,
            payment.Currency,
            payment.PaymentPurpose,
            payment.PaymentStatus,
            booking.BookingStatus.ToString(),
            booking.PaymentStatus,
            booking.DepositAmount,
            booking.RemainingAmount,
            payment.ProviderTransactionId,
            payment.CheckoutUrl,
            payment.QrCode,
            PaymentSupport.ResolvePaymentExpiresAt(payment),
            booking.HoldExpiresAt);

    public static SyncCharterBookingPaymentResult ToSyncPaymentResult(
        Booking booking,
        Payment payment) =>
        new(
            booking.Id,
            payment.Id,
            payment.PaymentCode,
            payment.Amount,
            payment.PaymentPurpose,
            payment.PaymentStatus,
            booking.BookingStatus.ToString(),
            booking.PaymentStatus,
            booking.DepositAmount,
            booking.RemainingAmount,
            payment.CheckoutUrl,
            payment.PaidAt,
            PaymentSupport.ResolvePaymentExpiresAt(payment),
            booking.HoldExpiresAt);

    public sealed record CharterBookingPaymentPlan(
        string Purpose,
        decimal Amount,
        decimal DepositAmount,
        decimal RemainingAmount);
}
