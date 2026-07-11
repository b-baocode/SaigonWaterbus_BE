using System.Globalization;
using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common;
using FluentValidation.Results;
using SaigonWaterbus.Application.Bookings;
using SaigonWaterbus.Application.Common.Exceptions;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Application.Promotions;
using SaigonWaterbus.Application.Tickets;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.Payments;

public sealed record CreatePaymentCommand(
    Guid BookingId,
    BookingPaymentOption PaymentOption = BookingPaymentOption.Full,
    decimal? DepositPercent = null,
    string? PromotionCode = null)
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

    public CreatePaymentCommandHandler(
        IApplicationDbContext context,
        IUserContext userContext,
        ICharterBookingPaymentGateway paymentGateway,
        IPaymentNotificationSender paymentNotificationSender,
        TimeProvider timeProvider,
        IPaymentProcessingLock? paymentProcessingLock = null,
        ITripSeatNotifier? tripSeatNotifier = null,
        IBookingTicketPdfRenderer? bookingTicketPdfRenderer = null)
    {
        _context = context;
        _userContext = userContext;
        _paymentGateway = paymentGateway;
        _paymentNotificationSender = paymentNotificationSender;
        _paymentProcessingLock = paymentProcessingLock ?? NullPaymentProcessingLock.Instance;
        _tripSeatNotifier = tripSeatNotifier ?? NullTripSeatNotifier.Instance;
        _timeProvider = timeProvider;
        _bookingTicketPdfRenderer = bookingTicketPdfRenderer;
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

        if (BookingSeatOccupancySupport.IsHoldExpired(booking, _timeProvider.GetUtcNow()))
        {
            await BookingHoldExpirySupport.ExpireBookingAsync(
                _context,
                _tripSeatNotifier,
                booking,
                cancellationToken);
            throw new ValidationException([new ValidationFailure("booking",
                "Booking đã hết hạn giữ chỗ (15 phút). Vui lòng đặt vé lại.")]);
        }

        await PaymentSupport.ApplyPromotionForCheckoutAsync(
            _context,
            _userContext,
            booking,
            request.PromotionCode,
            now,
            cancellationToken);

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
            _bookingTicketPdfRenderer);

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
                _bookingTicketPdfRenderer);
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

    public SyncPaymentCommandHandler(
        IApplicationDbContext context,
        IUserContext userContext,
        ICharterBookingPaymentGateway paymentGateway,
        IPaymentNotificationSender paymentNotificationSender,
        TimeProvider timeProvider,
        IPaymentProcessingLock? paymentProcessingLock = null,
        IBookingTicketPdfRenderer? bookingTicketPdfRenderer = null)
    {
        _context = context;
        _userContext = userContext;
        _paymentGateway = paymentGateway;
        _paymentNotificationSender = paymentNotificationSender;
        _paymentProcessingLock = paymentProcessingLock ?? NullPaymentProcessingLock.Instance;
        _timeProvider = timeProvider;
        _bookingTicketPdfRenderer = bookingTicketPdfRenderer;
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
            cancellationToken,
            _bookingTicketPdfRenderer);
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

    public HandlePaymentWebhookCommandHandler(
        IApplicationDbContext context,
        ICharterBookingPaymentGateway paymentGateway,
        IPaymentNotificationSender paymentNotificationSender,
        TimeProvider timeProvider,
        IPaymentProcessingLock? paymentProcessingLock = null,
        IBookingTicketPdfRenderer? bookingTicketPdfRenderer = null)
    {
        _context = context;
        _paymentGateway = paymentGateway;
        _paymentNotificationSender = paymentNotificationSender;
        _paymentProcessingLock = paymentProcessingLock ?? NullPaymentProcessingLock.Instance;
        _timeProvider = timeProvider;
        _bookingTicketPdfRenderer = bookingTicketPdfRenderer;
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
                cancellationToken,
                _bookingTicketPdfRenderer);
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
    private readonly ICharterBookingPaymentGateway _paymentGateway;
    private readonly TimeProvider _timeProvider;

    public RefundPaymentCommandHandler(
        IApplicationDbContext context,
        IUserContext userContext,
        ICharterBookingPaymentGateway paymentGateway,
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
        var refundAmount = await PaymentSupport.ResolvePolicyRefundAmountAsync(
            _context,
            payment,
            now,
            cancellationToken);
        var referenceId = PaymentSupport.CreateRefundReference(payment, now);
        payment.RefundStatus = PaymentSupport.RefundPendingStatus;
        payment.RefundRequestedAmount = refundAmount;
        payment.RefundMethod = PaymentSupport.PayOsProvider;
        payment.RefundReason = request.Reason.Trim();
        payment.RefundReferenceId = referenceId;
        payment.RefundFailureReason = null;
        payment.RefundProcessedByUserId = _userContext.UserId;
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
        await _context.SaveChangesAsync(cancellationToken);

        return PaymentSupport.ToDto(payment.Booking, payment);
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
        payment.RefundStatus = PaymentSupport.ManualRefundedStatus;
        payment.RefundFailureReason = null;
        payment.RefundProcessedByUserId = _userContext.UserId;
        payment.RefundedAt = request.RefundedAt ?? now;

        PaymentSupport.ApplyRefundStatus(payment.Booking);
        await _context.SaveChangesAsync(cancellationToken);

        return PaymentSupport.ToDto(payment.Booking, payment);
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
    public const string ManualRefundMethod = "Manual";
    public const string ManualRefundedStatus = "ManualRefunded";

    public static decimal ResolveRefundPercent(TimeSpan timeUntilDeparture) =>
        timeUntilDeparture >= TimeSpan.FromDays(3) ? 1.0m
        : timeUntilDeparture >= TimeSpan.FromHours(24) ? 0.7m
        : 0m;

    public static async Task<decimal> ResolvePolicyRefundAmountAsync(
        IApplicationDbContext context,
        Payment payment,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var departure = await ResolveDepartureAsync(context, payment.Booking, cancellationToken);
        var timeUntilDeparture = departure.HasValue ? departure.Value - now : TimeSpan.MaxValue;
        var refundPercent = ResolveRefundPercent(timeUntilDeparture);
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

        return refundAmount;
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
            var refundableAmount = payment.Amount - payment.RefundAmount;
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

    private static async Task<DateTimeOffset?> ResolveDepartureAsync(
        IApplicationDbContext context,
        Booking booking,
        CancellationToken cancellationToken)
    {
        if (Booking.IsCharterBookingType(booking.BookingType))
        {
            var startTime = booking.StartTime ?? new TimeOnly(0, 0);
            return new DateTimeOffset(booking.DepartureDate.GetValueOrDefault().ToDateTime(startTime), TimeSpan.Zero);
        }

        if (booking.TripId is Guid tripId)
        {
            return await context.Set<Trip>()
                .Where(t => t.Id == tripId)
                .Select(t => (DateTimeOffset?)t.DepartureTime)
                .SingleOrDefaultAsync(cancellationToken);
        }

        return null;
    }

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

        query = IncludeCharterBookingNotificationDetails(query);

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
            .Include(x => x.Booking.ItineraryStops)
                .ThenInclude(x => x.Station);

        var payment = await query.SingleOrDefaultAsync(x =>
                x.PaymentCode == paymentCode
                && x.Provider == PayOsProvider,
                cancellationToken)
            ?? throw new NotFoundException("Payment not found.");
        if (payment.Booking.UserId != userId)
        {
            throw new NotFoundException("Payment not found.");
        }

        return payment;
    }

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

        var normalizedCode = promotionCode.Trim().ToUpperInvariant();
        var hasLockedPayment = booking.Payments.Any(x =>
            IsPayOsPayment(x) && (IsPending(x.PaymentStatus) || IsPaid(x.PaymentStatus)));
        if (hasLockedPayment && string.IsNullOrWhiteSpace(normalizedCode))
        {
            throw new ValidationException([new ValidationFailure(nameof(promotionCode),
                "Không thể đổi mã giảm giá khi booking đã có payment đang chờ hoặc đã thanh toán.")]);
        }

        if (string.IsNullOrWhiteSpace(normalizedCode))
        {
            await ReplaceBookingPromotionAsync(
                context,
                booking,
                promotion: null,
                cancellationToken);
            return;
        }

        var promotion = await context.Set<Promotion>()
            .SingleOrDefaultAsync(p => p.PromotionCode == normalizedCode, cancellationToken)
            ?? throw new ValidationException([new ValidationFailure(nameof(promotionCode),
                "Promotion code not found.")]);

        if (booking.PromotionId == promotion.Id)
        {
            return;
        }

        if (hasLockedPayment)
        {
            throw new ValidationException([new ValidationFailure(nameof(promotionCode),
                "Không thể đổi mã giảm giá khi booking đã có payment đang chờ hoặc đã thanh toán.")]);
        }

        EnsurePromotionCanApplyAtCheckout(promotion, booking.SubtotalAmount, now, nameof(promotionCode));

        var userId = userContext.UserId
            ?? throw new ValidationException([new ValidationFailure("userId", "User must be authenticated.")]);

        await PromotionUsageSupport.EnsureAccountCanUsePromotionAsync(
            context,
            promotion,
            userId,
            nameof(promotionCode),
            cancellationToken,
            booking.Id);

        await ReplaceBookingPromotionAsync(context, booking, promotion, cancellationToken);
    }

    private static void EnsurePromotionCanApplyAtCheckout(
        Promotion promotion,
        decimal subtotalAmount,
        DateTimeOffset now,
        string propertyName)
    {
        if (promotion.Status != "Active" || promotion.ValidFrom > now || promotion.ValidTo < now
            || (promotion.UsageLimit.HasValue && promotion.UsageCount >= promotion.UsageLimit))
        {
            throw new ValidationException([new ValidationFailure(propertyName,
                "Promotion code is not applicable.")]);
        }

        if (promotion.MinOrderValue.HasValue && subtotalAmount < promotion.MinOrderValue)
        {
            throw new ValidationException([new ValidationFailure(propertyName,
                $"Minimum order value is {promotion.MinOrderValue:N0}.")]);
        }
    }

    private static async Task ReplaceBookingPromotionAsync(
        IApplicationDbContext context,
        Booking booking,
        Promotion? promotion,
        CancellationToken cancellationToken)
    {
        if (booking.PromotionId == promotion?.Id)
        {
            return;
        }

        if (booking.PromotionId.HasValue)
        {
            var previousPromotion = await context.Set<Promotion>()
                .SingleOrDefaultAsync(x => x.Id == booking.PromotionId.Value, cancellationToken);
            PromotionUsageSupport.DecrementUsage(previousPromotion);
        }

        booking.PromotionId = promotion?.Id;
        booking.Promotion = promotion;
        booking.DiscountAmount = promotion is null
            ? 0
            : promotion.PromotionType == PromotionType.Percent
                ? Math.Min(booking.SubtotalAmount * promotion.DiscountValue / 100, booking.SubtotalAmount)
                : Math.Min(promotion.DiscountValue, booking.SubtotalAmount);
        booking.TotalAmount = booking.SubtotalAmount - booking.DiscountAmount;
        booking.DepositAmount = 0;
        booking.RemainingAmount = booking.TotalAmount;
        booking.PaymentStatus = UnpaidBookingPaymentStatus;

        PromotionUsageSupport.IncrementUsage(promotion);
        await context.SaveChangesAsync(cancellationToken);
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

    public static string CreateManualRefundReference(Payment payment, DateTimeOffset now) =>
        $"MRF{now.ToUnixTimeMilliseconds()}{payment.Id.ToString("N")[..8]}";

    public static bool IsPayOsPayment(Payment payment) =>
        string.Equals(payment.Provider, PayOsProvider, StringComparison.OrdinalIgnoreCase);

    public static bool IsPending(string status) =>
        string.Equals(status, PendingStatus, StringComparison.OrdinalIgnoreCase);

    public static bool IsPaid(string status) =>
        string.Equals(status, PaidStatus, StringComparison.OrdinalIgnoreCase);

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
        CancellationToken cancellationToken,
        IBookingTicketPdfRenderer? bookingTicketPdfRenderer = null)
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
        }

        if (wasPaid || !isPaid || !payment.PaidAt.HasValue)
        {
            return;
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
            stops);
    }

    private static IQueryable<Booking> IncludeCharterBookingNotificationDetails(IQueryable<Booking> query) =>
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
            payment.RefundedAt);

    public sealed record PaymentPlan(
        string Purpose,
        decimal Amount,
        decimal DepositAmount,
        decimal RemainingAmount);
}
