using System.Globalization;
using FluentValidation.Results;
using SaigonWaterbus.Application.Common.Exceptions;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Application.Tickets;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.Payments;

public sealed record CreatePaymentCommand(
    Guid BookingId,
    BookingPaymentOption PaymentOption = BookingPaymentOption.Full,
    decimal? DepositPercent = null)
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
    }
}

public sealed class CreatePaymentCommandHandler : IRequestHandler<CreatePaymentCommand, PaymentDto>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;
    private readonly ICustomBookingPaymentGateway _paymentGateway;
    private readonly IPaymentNotificationSender _paymentNotificationSender;
    private readonly IPaymentProcessingLock _paymentProcessingLock;
    private readonly TimeProvider _timeProvider;

    public CreatePaymentCommandHandler(
        IApplicationDbContext context,
        IUserContext userContext,
        ICustomBookingPaymentGateway paymentGateway,
        IPaymentNotificationSender paymentNotificationSender,
        TimeProvider timeProvider,
        IPaymentProcessingLock? paymentProcessingLock = null)
    {
        _context = context;
        _userContext = userContext;
        _paymentGateway = paymentGateway;
        _paymentNotificationSender = paymentNotificationSender;
        _paymentProcessingLock = paymentProcessingLock ?? NullPaymentProcessingLock.Instance;
        _timeProvider = timeProvider;
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

        PaymentSupport.EnsureCanCreatePayment(booking);

        var existingPendingPayment = booking.Payments
            .Where(PaymentSupport.IsPayOsPayment)
            .OrderByDescending(x => x.Created)
            .FirstOrDefault(x =>
                PaymentSupport.IsPending(x.PaymentStatus)
                && !string.IsNullOrWhiteSpace(x.CheckoutUrl));
        if (existingPendingPayment is not null)
        {
            return PaymentSupport.ToDto(booking, existingPendingPayment);
        }

        var paidAmount = PaymentSupport.GetPaidAmount(booking);
        if (paidAmount >= booking.TotalAmount)
        {
            var latestPaidPayment = booking.Payments
                .Where(PaymentSupport.IsPayOsPayment)
                .OrderByDescending(x => x.Created)
                .First(x => PaymentSupport.IsPaid(x.PaymentStatus));
            return PaymentSupport.ToDto(booking, latestPaidPayment);
        }

        var paymentPlan = PaymentSupport.ResolvePaymentPlan(
            booking,
            request.PaymentOption,
            request.DepositPercent,
            paidAmount);
        var now = _timeProvider.GetUtcNow();
        var orderCode = await PaymentSupport.GeneratePaymentOrderCodeAsync(_context, now, cancellationToken);
        var amount = PaymentSupport.ToPayOsAmount(
            paymentPlan.Amount,
            nameof(paymentPlan.Amount),
            "Số tiền thanh toán phải là số nguyên VND lớn hơn 0.");

        var payment = new Payment
        {
            BookingId = booking.Id,
            PaymentCode = orderCode.ToString(CultureInfo.InvariantCulture),
            Provider = PaymentSupport.PayOsProvider,
            Amount = paymentPlan.Amount,
            Currency = booking.Currency,
            PaymentMethod = PaymentSupport.PayOsProvider,
            PaymentPurpose = paymentPlan.Purpose,
            PaymentStatus = PaymentSupport.PendingStatus
        };
        booking.Payments.Add(payment);
        _context.Set<Payment>().Add(payment);
        booking.PaymentStatus = PaymentSupport.UnpaidBookingPaymentStatus;
        booking.DepositAmount = paymentPlan.DepositAmount;
        booking.RemainingAmount = paymentPlan.RemainingAmount;
        await _context.SaveChangesAsync(cancellationToken);

        CustomBookingDepositPaymentResult paymentResult;
        try
        {
            paymentResult = await _paymentGateway.CreateDepositPaymentAsync(
                new CustomBookingDepositPaymentRequest(
                    orderCode,
                    amount,
                    PaymentSupport.CreatePaymentDescription(booking),
                    booking.ContactName,
                    booking.ContactEmail,
                    booking.ContactPhone,
                    $"{paymentPlan.Purpose} booking {booking.BookingCode}",
                    null),
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
            cancellationToken);

        return PaymentSupport.ToDto(booking, payment);
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
                cancellationToken);
            return true;
        }
        catch (PaymentGatewayException)
        {
            return false;
        }
    }
}

public sealed record SyncPaymentCommand(Guid PaymentId) : IRequest<PaymentDto>;

public sealed class SyncPaymentCommandValidator : AbstractValidator<SyncPaymentCommand>
{
    public SyncPaymentCommandValidator()
    {
        RuleFor(x => x.PaymentId).NotEmpty();
    }
}

public sealed class SyncPaymentCommandHandler : IRequestHandler<SyncPaymentCommand, PaymentDto>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;
    private readonly ICustomBookingPaymentGateway _paymentGateway;
    private readonly IPaymentNotificationSender _paymentNotificationSender;
    private readonly IPaymentProcessingLock _paymentProcessingLock;
    private readonly TimeProvider _timeProvider;

    public SyncPaymentCommandHandler(
        IApplicationDbContext context,
        IUserContext userContext,
        ICustomBookingPaymentGateway paymentGateway,
        IPaymentNotificationSender paymentNotificationSender,
        TimeProvider timeProvider,
        IPaymentProcessingLock? paymentProcessingLock = null)
    {
        _context = context;
        _userContext = userContext;
        _paymentGateway = paymentGateway;
        _paymentNotificationSender = paymentNotificationSender;
        _paymentProcessingLock = paymentProcessingLock ?? NullPaymentProcessingLock.Instance;
        _timeProvider = timeProvider;
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

        await using var paymentLock = await _paymentProcessingLock.TryAcquireAsync(
            payment.PaymentCode,
            cancellationToken);
        if (!paymentLock.Acquired)
        {
            throw new ValidationException([new ValidationFailure("payment",
                "Payment đang được đồng bộ. Vui lòng thử lại sau.")]);
        }

        CustomBookingPaymentStatusResult paymentStatus;
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
        PaymentSupport.ApplyPaymentStatus(
            payment.Booking,
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
            payment.Booking,
            payment,
            wasPaid,
            cancellationToken);

        return PaymentSupport.ToDto(payment.Booking, payment);
    }
}

public sealed record HandlePaymentWebhookCommand(CustomBookingDepositPaymentWebhook Webhook)
    : IRequest<PaymentWebhookResult>;

public sealed class HandlePaymentWebhookCommandHandler
    : IRequestHandler<HandlePaymentWebhookCommand, PaymentWebhookResult>
{
    private readonly IApplicationDbContext _context;
    private readonly ICustomBookingPaymentGateway _paymentGateway;
    private readonly IPaymentNotificationSender _paymentNotificationSender;
    private readonly IPaymentProcessingLock _paymentProcessingLock;
    private readonly TimeProvider _timeProvider;

    public HandlePaymentWebhookCommandHandler(
        IApplicationDbContext context,
        ICustomBookingPaymentGateway paymentGateway,
        IPaymentNotificationSender paymentNotificationSender,
        TimeProvider timeProvider,
        IPaymentProcessingLock? paymentProcessingLock = null)
    {
        _context = context;
        _paymentGateway = paymentGateway;
        _paymentNotificationSender = paymentNotificationSender;
        _paymentProcessingLock = paymentProcessingLock ?? NullPaymentProcessingLock.Instance;
        _timeProvider = timeProvider;
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
            PaymentSupport.ApplyPaymentStatus(
                payment.Booking,
                payment,
                PaymentSupport.PaidStatus,
                webhook.Data.PaymentLinkId,
                payment.CheckoutUrl,
                _timeProvider.GetUtcNow());
            await _context.SaveChangesAsync(cancellationToken);
            await PaymentSupport.SendPaymentNotificationIfPaidAsync(
                _context,
                _timeProvider,
                _paymentNotificationSender,
                payment.Booking,
                payment,
                wasPaid,
                cancellationToken);
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

public sealed record RefundPaymentCommand(
    Guid PaymentId,
    string Reason,
    string BankBin,
    string AccountNumber,
    string AccountName)
    : IRequest<PaymentDto>;

public sealed class RefundPaymentCommandValidator : AbstractValidator<RefundPaymentCommand>
{
    public RefundPaymentCommandValidator()
    {
        RuleFor(x => x.PaymentId).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(100);
        RuleFor(x => x.BankBin).NotEmpty().Length(6).Matches("^[0-9]{6}$");
        RuleFor(x => x.AccountNumber).NotEmpty().MaximumLength(50).Matches("^[0-9]+$");
        RuleFor(x => x.AccountName).NotEmpty().MaximumLength(100);
    }
}

public sealed class RefundPaymentCommandHandler : IRequestHandler<RefundPaymentCommand, PaymentDto>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;
    private readonly ICustomBookingPaymentGateway _paymentGateway;
    private readonly TimeProvider _timeProvider;

    public RefundPaymentCommandHandler(
        IApplicationDbContext context,
        IUserContext userContext,
        ICustomBookingPaymentGateway paymentGateway,
        TimeProvider timeProvider)
    {
        _context = context;
        _userContext = userContext;
        _paymentGateway = paymentGateway;
        _timeProvider = timeProvider;
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
            throw new ValidationException([new ValidationFailure(nameof(payment.PaymentStatus),
                "Chỉ có thể hoàn tiền cho payment đã thanh toán.")]);
        }

        var now = _timeProvider.GetUtcNow();

        var departure = await ResolveDepartureAsync(payment.Booking, cancellationToken);
        var timeUntilDeparture = departure.HasValue ? departure.Value - now : TimeSpan.MaxValue;
        var refundPercent = PaymentSupport.ResolveRefundPercent(timeUntilDeparture);
        if (refundPercent <= 0)
        {
            throw new ValidationException([new ValidationFailure("refund",
                "Theo chính sách hoàn tiền, hủy dưới 24 giờ trước giờ khởi hành sẽ không được hoàn.")]);
        }

        var refundableAmount = payment.Amount - payment.RefundAmount;
        var refundAmount = Math.Min(Math.Floor(payment.Amount * refundPercent), refundableAmount);

        if (refundAmount <= 0)
        {
            throw new ValidationException([new ValidationFailure("refund",
                "Không còn số tiền hợp lệ để hoàn theo chính sách.")]);
        }
        var referenceId = PaymentSupport.CreateRefundReference(payment, now);
        payment.RefundStatus = PaymentSupport.RefundPendingStatus;
        payment.RefundReferenceId = referenceId;
        payment.RefundFailureReason = null;
        await _context.SaveChangesAsync(cancellationToken);

        CustomBookingRefundPayoutResult refundResult;
        try
        {
            refundResult = await _paymentGateway.CreateRefundPayoutAsync(
                new CustomBookingRefundPayoutRequest(
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
        await _context.SaveChangesAsync(cancellationToken);

        return PaymentSupport.ToDto(payment.Booking, payment);
    }

    private async Task<DateTimeOffset?> ResolveDepartureAsync(Booking booking, CancellationToken cancellationToken)
    {
        if (booking.BookingType == Booking.CustomBookingType)
        {
            var startTime = booking.StartTime ?? new TimeOnly(0, 0);
            return new DateTimeOffset(booking.DepartureDate.GetValueOrDefault().ToDateTime(startTime), TimeSpan.Zero);
        }

        if (booking.TripId is Guid tripId)
        {
            return await _context.Set<Trip>()
                .Where(t => t.Id == tripId)
                .Select(t => (DateTimeOffset?)t.DepartureTime)
                .SingleOrDefaultAsync(cancellationToken);
        }

        return null;
    }
}

internal static class PaymentSupport
{
    public const string PayOsProvider = "PayOS";
    public const string PendingStatus = "Pending";
    public const string PaidStatus = "Paid";
    public const string CancelledStatus = "Cancelled";
    public const string FailedStatus = "Failed";
    public const string ExpiredStatus = "Expired";
    public const string RefundedStatus = "Refunded";

    public static decimal ResolveRefundPercent(TimeSpan timeUntilDeparture) =>
        timeUntilDeparture >= TimeSpan.FromDays(3) ? 1.0m
        : timeUntilDeparture >= TimeSpan.FromHours(24) ? 0.7m
        : 0m;
    public const string PartiallyRefundedStatus = "PartiallyRefunded";
    public const string UnpaidBookingPaymentStatus = "Unpaid";
    public const string DepositPaidBookingPaymentStatus = "DepositPaid";
    public const string PaidBookingPaymentStatus = "Paid";
    public const string RefundedBookingPaymentStatus = "Refunded";
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

        query = IncludeCustomBookingNotificationDetails(query);

        var booking = await query.SingleOrDefaultAsync(x => x.Id == bookingId, cancellationToken)
            ?? throw new NotFoundException("Booking not found.");
        if (booking.UserId != userId)
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
            .Include(x => x.Booking.ItineraryStops)
                .ThenInclude(x => x.Station);

        var payment = await query.SingleOrDefaultAsync(x => x.Id == paymentId, cancellationToken)
            ?? throw new NotFoundException("Payment not found.");
        if (payment.Booking.UserId != userId)
        {
            throw new NotFoundException("Payment not found.");
        }

        return payment;
    }

    public static void EnsureCanCreatePayment(Booking booking)
    {
        if (booking.BookingStatus == BookingStatus.Cancelled)
        {
            throw new ValidationException([new ValidationFailure(nameof(booking.BookingStatus),
                "Không thể thanh toán booking đã hủy.")]);
        }

        if (booking.BookingType == Booking.CustomBookingType && booking.BookingStatus == BookingStatus.PendingQuote)
        {
            throw new ValidationException([new ValidationFailure(nameof(booking.BookingStatus),
                "Custom booking chưa được admin nhập tàu và chốt giá.")]);
        }

        if (booking.BookingStatus is BookingStatus.Completed or BookingStatus.Refunded or BookingStatus.Expired)
        {
            throw new ValidationException([new ValidationFailure(nameof(booking.BookingStatus),
                "Không thể tạo thanh toán cho booking đã hoàn tất, đã hoàn tiền hoặc đã hết hạn giữ chỗ.")]);
        }

        if (booking.TotalAmount <= 0)
        {
            throw new ValidationException([new ValidationFailure(nameof(booking.TotalAmount),
                "Booking chưa có số tiền cần thanh toán.")]);
        }
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

        if (booking.BookingType != Booking.CustomBookingType)
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

        if (!IsPaid(payment.PaymentStatus))
        {
            return;
        }

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

    public static void ApplyRefundStatus(Booking booking)
    {
        var paidAmount = GetPaidAmount(booking);
        var refundedAmount = booking.Payments
            .Where(IsPayOsPayment)
            .Sum(x => x.RefundAmount);

        if (refundedAmount <= 0)
        {
            return;
        }

        if (refundedAmount >= paidAmount)
        {
            booking.PaymentStatus = RefundedBookingPaymentStatus;
            booking.BookingStatus = BookingStatus.Refunded;
            foreach (var payment in booking.Payments.Where(x => IsPayOsPayment(x) && IsPaid(x.PaymentStatus)))
            {
                if (payment.RefundAmount >= payment.Amount)
                {
                    payment.PaymentStatus = RefundedStatus;
                }
            }
            return;
        }

        booking.PaymentStatus = PartiallyRefundedStatus;
    }

    public static async Task SendPaymentNotificationIfPaidAsync(
        IApplicationDbContext context,
        TimeProvider timeProvider,
        IPaymentNotificationSender paymentNotificationSender,
        Booking booking,
        Payment payment,
        bool wasPaid,
        CancellationToken cancellationToken)
    {
        var isPaid = IsPaid(payment.PaymentStatus);
        if (isPaid)
        {
            await TicketIssueSupport.EnsureRegularBookingPassengerTicketsAsync(
                context,
                booking,
                timeProvider,
                cancellationToken);
        }

        if (wasPaid || !isPaid || string.IsNullOrWhiteSpace(booking.ContactEmail))
        {
            return;
        }

        if (!payment.PaidAt.HasValue)
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

        if (!payment.PaidAt.HasValue)
        {
            throw new InvalidOperationException("Payment paid time is required to create a payment notification.");
        }

        var contactName = string.IsNullOrWhiteSpace(booking.ContactName)
            ? "Quy khach"
            : booking.ContactName.Trim();
        var isFullyPaid = string.Equals(
                booking.PaymentStatus,
                PaidBookingPaymentStatus,
                StringComparison.OrdinalIgnoreCase)
            || booking.RemainingAmount <= 0;
        var isCustomBooking = booking.BookingType == Booking.CustomBookingType;
        var stops = isCustomBooking
            ? booking.ItineraryStops
                .OrderBy(x => x.StopOrder)
                .Select(x => new PaymentNotificationStop(
                    x.Station.StationName,
                    x.Note,
                    x.StayDurationMinutes))
                .ToList()
            : [];

        return new PaymentSucceededNotification(
            booking.ContactEmail.Trim(),
            contactName,
            booking.ContactPhone,
            booking.BookingCode,
            isCustomBooking ? "CustomBooking" : "Booking",
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
            isCustomBooking ? booking.DepartureDate : null,
            isCustomBooking ? booking.StartTime : null,
            isCustomBooking ? booking.RentalUnit?.ToString() : null,
            isCustomBooking ? booking.DurationValue.GetValueOrDefault() : 0,
            isCustomBooking ? booking.PassengerCount.GetValueOrDefault() : booking.Passengers.Count,
            isCustomBooking ? booking.Boat?.Name : null,
            isCustomBooking ? booking.FromStation?.StationName : null,
            ResolveStationAddress(isCustomBooking ? booking.FromStation : null),
            isCustomBooking ? booking.ToStation?.StationName : null,
            ResolveStationAddress(isCustomBooking ? booking.ToStation : null),
            stops);
    }

    private static IQueryable<Booking> IncludeCustomBookingNotificationDetails(IQueryable<Booking> query) =>
        query
            .Include(x => x.Boat)
            .Include(x => x.FromStation)
            .Include(x => x.ToStation)
            .Include(x => x.ItineraryStops)
                .ThenInclude(x => x.Station);

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
            payment.RefundAmount,
            payment.RefundReferenceId,
            payment.RefundPayoutId,
            payment.RefundStatus,
            payment.RefundFailureReason,
            payment.RefundedAt);

    public sealed record PaymentPlan(
        string Purpose,
        decimal Amount,
        decimal DepositAmount,
        decimal RemainingAmount);
}
