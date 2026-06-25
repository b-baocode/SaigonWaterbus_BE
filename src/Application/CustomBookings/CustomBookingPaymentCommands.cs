using System.Globalization;
using FluentValidation.Results;
using SaigonWaterbus.Application.Common.Exceptions;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.CustomBookings;

public sealed record CreateCustomBookingPaymentCommand(
    Guid BookingId,
    CustomBookingPaymentOption PaymentOption = CustomBookingPaymentOption.Deposit,
    decimal? DepositPercent = null)
    : IRequest<CreateCustomBookingPaymentResult>;

public sealed class CreateCustomBookingPaymentCommandValidator
    : AbstractValidator<CreateCustomBookingPaymentCommand>
{
    public CreateCustomBookingPaymentCommandValidator()
    {
        RuleFor(x => x.BookingId).NotEmpty();
        RuleFor(x => x.PaymentOption).IsInEnum();
        RuleFor(x => x.DepositPercent)
            .GreaterThan(0)
            .LessThan(100)
            .When(x => x.PaymentOption == CustomBookingPaymentOption.Deposit && x.DepositPercent.HasValue)
            .WithMessage("Phần trăm đặt cọc phải lớn hơn 0 và nhỏ hơn 100.");
    }
}

public sealed class CreateCustomBookingPaymentCommandHandler
    : IRequestHandler<CreateCustomBookingPaymentCommand, CreateCustomBookingPaymentResult>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;
    private readonly ICustomBookingPaymentGateway _paymentGateway;
    private readonly TimeProvider _timeProvider;

    public CreateCustomBookingPaymentCommandHandler(
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

    public async Task<CreateCustomBookingPaymentResult> Handle(
        CreateCustomBookingPaymentCommand request,
        CancellationToken cancellationToken)
    {
        var booking = await CustomBookingPaymentSupport.GetOwnedCustomBookingAsync(
            _context,
            _userContext,
            request.BookingId,
            includePayments: true,
            cancellationToken);

        CustomBookingPaymentSupport.EnsureCanCreatePayment(booking);

        var existingPendingPayment = booking.Payments
            .Where(CustomBookingPaymentSupport.IsPayOsPayment)
            .OrderByDescending(x => x.Created)
            .FirstOrDefault(x =>
                CustomBookingPaymentSupport.IsPending(x.PaymentStatus)
                && !string.IsNullOrWhiteSpace(x.CheckoutUrl));
        if (existingPendingPayment is not null)
        {
            return CustomBookingPaymentSupport.ToCreatePaymentResult(booking, existingPendingPayment);
        }

        var paidAmount = CustomBookingPaymentSupport.GetPaidAmount(booking);
        if (paidAmount >= booking.TotalAmount)
        {
            var latestPaidPayment = booking.Payments
                .Where(CustomBookingPaymentSupport.IsPayOsPayment)
                .OrderByDescending(x => x.Created)
                .First(x => CustomBookingPaymentSupport.IsPaid(x.PaymentStatus));
            return CustomBookingPaymentSupport.ToCreatePaymentResult(booking, latestPaidPayment);
        }

        var paymentPlan = CustomBookingPaymentSupport.ResolvePaymentPlan(
            booking,
            request.PaymentOption,
            request.DepositPercent,
            paidAmount);
        var now = _timeProvider.GetUtcNow();
        var orderCode = await CustomBookingPaymentSupport.GeneratePaymentOrderCodeAsync(
            _context,
            now,
            cancellationToken);
        var amount = CustomBookingPaymentSupport.ToPayOsAmount(
            paymentPlan.Amount,
            nameof(paymentPlan.Amount),
            "Tổng tiền thanh toán phải là số nguyên VND lớn hơn 0.");

        var payment = new Payment
        {
            BookingId = booking.Id,
            PaymentCode = orderCode.ToString(CultureInfo.InvariantCulture),
            Provider = CustomBookingPaymentSupport.PayOsProvider,
            Amount = paymentPlan.Amount,
            Currency = booking.Currency,
            PaymentMethod = CustomBookingPaymentSupport.PayOsProvider,
            PaymentPurpose = paymentPlan.Purpose,
            PaymentStatus = CustomBookingPaymentSupport.PendingStatus
        };
        booking.Payments.Add(payment);
        _context.Set<Payment>().Add(payment);
        booking.PaymentStatus = CustomBookingPaymentSupport.UnpaidBookingPaymentStatus;
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
                    CustomBookingPaymentSupport.CreatePaymentDescription(booking),
                    booking.ContactName,
                    booking.ContactEmail,
                    booking.ContactPhone,
                    $"{paymentPlan.Purpose} custom booking {booking.BookingCode}",
                    CustomBookingPaymentSupport.ResolvePaymentExpiredAt(booking)),
                cancellationToken);
        }
        catch (PaymentGatewayException ex)
        {
            if (await TryRecoverCreatedPaymentAsync(booking, payment, orderCode, amount, cancellationToken))
            {
                return CustomBookingPaymentSupport.ToCreatePaymentResult(booking, payment);
            }

            payment.PaymentStatus = CustomBookingPaymentSupport.FailedStatus;
            CustomBookingPaymentSupport.RestorePaymentSummaryFromPaidPayments(booking);
            await _context.SaveChangesAsync(cancellationToken);
            throw new ValidationException([new ValidationFailure("payment", ex.Message)]);
        }

        payment.ProviderTransactionId = paymentResult.PaymentLinkId;
        payment.CheckoutUrl = paymentResult.CheckoutUrl;
        payment.QrCode = paymentResult.QrCode;
        payment.PaymentStatus = CustomBookingPaymentSupport.ResolvePaymentStatus(paymentResult.Status);
        await _context.SaveChangesAsync(cancellationToken);

        return CustomBookingPaymentSupport.ToCreatePaymentResult(booking, payment);
    }

    private async Task<bool> TryRecoverCreatedPaymentAsync(
        CustomBooking booking,
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

            CustomBookingPaymentSupport.ApplyPaymentStatus(
                booking,
                payment,
                paymentStatus.Status,
                paymentStatus.PaymentLinkId,
                paymentStatus.CheckoutUrl,
                _timeProvider.GetUtcNow());
            await _context.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (PaymentGatewayException)
        {
            return false;
        }
    }
}

public sealed record SyncCustomBookingPaymentCommand(Guid BookingId)
    : IRequest<SyncCustomBookingPaymentResult>;

public sealed class SyncCustomBookingPaymentCommandValidator
    : AbstractValidator<SyncCustomBookingPaymentCommand>
{
    public SyncCustomBookingPaymentCommandValidator()
    {
        RuleFor(x => x.BookingId).NotEmpty();
    }
}

public sealed class SyncCustomBookingPaymentCommandHandler
    : IRequestHandler<SyncCustomBookingPaymentCommand, SyncCustomBookingPaymentResult>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;
    private readonly ICustomBookingPaymentGateway _paymentGateway;
    private readonly TimeProvider _timeProvider;

    public SyncCustomBookingPaymentCommandHandler(
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

    public async Task<SyncCustomBookingPaymentResult> Handle(
        SyncCustomBookingPaymentCommand request,
        CancellationToken cancellationToken)
    {
        var booking = await CustomBookingPaymentSupport.GetOwnedCustomBookingAsync(
            _context,
            _userContext,
            request.BookingId,
            includePayments: true,
            cancellationToken);

        var payment = CustomBookingPaymentSupport.GetLatestPayOsPayment(booking)
            ?? throw new NotFoundException("Không tìm thấy thanh toán PayOS cho custom booking.");

        if (!long.TryParse(payment.PaymentCode, NumberStyles.None, CultureInfo.InvariantCulture, out var orderCode))
        {
            throw new ValidationException([new ValidationFailure(nameof(payment.PaymentCode),
                "Mã thanh toán PayOS không hợp lệ.")]);
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

        var expectedAmount = CustomBookingPaymentSupport.ToPayOsAmount(
            payment.Amount,
            nameof(payment.Amount),
            "Số tiền thanh toán phải là số nguyên VND lớn hơn 0.");
        if (paymentStatus.Amount.HasValue && paymentStatus.Amount.Value != expectedAmount)
        {
            payment.PaymentStatus = CustomBookingPaymentSupport.FailedStatus;
            await _context.SaveChangesAsync(cancellationToken);
            throw new ValidationException([new ValidationFailure("amount",
                "Số tiền thanh toán PayOS không khớp booking.")]);
        }

        CustomBookingPaymentSupport.ApplyPaymentStatus(
            booking,
            payment,
            paymentStatus.Status,
            paymentStatus.PaymentLinkId,
            paymentStatus.CheckoutUrl,
            _timeProvider.GetUtcNow());
        await _context.SaveChangesAsync(cancellationToken);

        return CustomBookingPaymentSupport.ToSyncPaymentResult(booking, payment);
    }
}

public sealed record HandleCustomBookingPaymentWebhookCommand(
    CustomBookingDepositPaymentWebhook Webhook)
    : IRequest<CustomBookingPaymentWebhookResult>;

public sealed class HandleCustomBookingPaymentWebhookCommandHandler
    : IRequestHandler<HandleCustomBookingPaymentWebhookCommand, CustomBookingPaymentWebhookResult>
{
    private readonly IApplicationDbContext _context;
    private readonly ICustomBookingPaymentGateway _paymentGateway;
    private readonly TimeProvider _timeProvider;

    public HandleCustomBookingPaymentWebhookCommandHandler(
        IApplicationDbContext context,
        ICustomBookingPaymentGateway paymentGateway,
        TimeProvider timeProvider)
    {
        _context = context;
        _paymentGateway = paymentGateway;
        _timeProvider = timeProvider;
    }

    public async Task<CustomBookingPaymentWebhookResult> Handle(
        HandleCustomBookingPaymentWebhookCommand request,
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
                && x.Provider == CustomBookingPaymentSupport.PayOsProvider,
                cancellationToken);

        if (payment is null)
        {
            return new CustomBookingPaymentWebhookResult(
                false,
                webhook.Data.OrderCode,
                null,
                "Không tìm thấy orderCode trong hệ thống.");
        }

        if (payment.Booking is not CustomBooking booking)
        {
            return new CustomBookingPaymentWebhookResult(
                false,
                webhook.Data.OrderCode,
                payment.PaymentStatus,
                "OrderCode không thuộc custom booking.");
        }

        var expectedAmount = CustomBookingPaymentSupport.ToPayOsAmount(
            payment.Amount,
            nameof(payment.Amount),
            "Số tiền thanh toán phải là số nguyên VND lớn hơn 0.");
        var isPaid = webhook.Success
            && string.Equals(webhook.Code, "00", StringComparison.Ordinal)
            && string.Equals(webhook.Data.Code, "00", StringComparison.Ordinal);

        if (isPaid && webhook.Data.Amount != expectedAmount)
        {
            payment.PaymentStatus = CustomBookingPaymentSupport.FailedStatus;
            await _context.SaveChangesAsync(cancellationToken);
            throw new ValidationException([new ValidationFailure("amount",
                "Số tiền webhook PayOS không khớp booking.")]);
        }

        if (isPaid)
        {
            CustomBookingPaymentSupport.ApplyPaymentStatus(
                booking,
                payment,
                CustomBookingPaymentSupport.PaidStatus,
                webhook.Data.PaymentLinkId,
                payment.CheckoutUrl,
                _timeProvider.GetUtcNow());
            await _context.SaveChangesAsync(cancellationToken);

            return new CustomBookingPaymentWebhookResult(
                true,
                webhook.Data.OrderCode,
                payment.PaymentStatus,
                "Đã ghi nhận thanh toán custom booking.");
        }

        if (!CustomBookingPaymentSupport.IsPaid(payment.PaymentStatus))
        {
            payment.PaymentStatus = CustomBookingPaymentSupport.FailedStatus;
            await _context.SaveChangesAsync(cancellationToken);
        }

        return new CustomBookingPaymentWebhookResult(
            true,
            webhook.Data.OrderCode,
            payment.PaymentStatus,
            "Webhook PayOS không phải trạng thái thanh toán thành công.");
    }
}

internal static class CustomBookingPaymentSupport
{
    public const string PayOsProvider = "PayOS";
    public const string PendingStatus = "Pending";
    public const string PaidStatus = "Paid";
    public const string CancelledStatus = "Cancelled";
    public const string FailedStatus = "Failed";
    public const string ExpiredStatus = "Expired";
    public const string UnpaidBookingPaymentStatus = "Unpaid";
    public const string DepositPaidBookingPaymentStatus = "DepositPaid";
    public const string PaidBookingPaymentStatus = "Paid";
    public const string DepositPurpose = "Deposit";
    public const string FullPurpose = "Full";
    public const string RemainingPurpose = "Remaining";
    private const decimal DefaultDepositPercent = 50m;

    public static async Task<CustomBooking> GetOwnedCustomBookingAsync(
        IApplicationDbContext context,
        IUserContext userContext,
        Guid bookingId,
        bool includePayments,
        CancellationToken cancellationToken)
    {
        var userId = userContext.UserId
            ?? throw new ValidationException([new ValidationFailure("userId", "User must be authenticated.")]);

        IQueryable<CustomBooking> query = context.Set<CustomBooking>();
        if (includePayments)
        {
            query = query.Include(x => x.Payments);
        }

        var booking = await query.SingleOrDefaultAsync(x => x.Id == bookingId, cancellationToken)
            ?? throw new NotFoundException("Custom booking not found.");

        if (booking.UserId != userId)
        {
            throw new NotFoundException("Custom booking not found.");
        }

        return booking;
    }

    public static void EnsureCanCreatePayment(CustomBooking booking)
    {
        if (booking.BookingStatus == BookingStatus.Cancelled)
        {
            throw new ValidationException([new ValidationFailure(nameof(booking.BookingStatus),
                "Không thể thanh toán custom booking đã hủy.")]);
        }

        if (booking.BookingStatus == BookingStatus.PendingQuote)
        {
            throw new ValidationException([new ValidationFailure(nameof(booking.BookingStatus),
                "Custom booking chưa được admin nhập tàu và chốt giá.")]);
        }

        if (booking.BookingStatus is BookingStatus.Completed or BookingStatus.Refunded)
        {
            throw new ValidationException([new ValidationFailure(nameof(booking.BookingStatus),
                "Không thể tạo thanh toán cho custom booking đã hoàn tất.")]);
        }

        if (booking.TotalAmount <= 0)
        {
            throw new ValidationException([new ValidationFailure(nameof(booking.TotalAmount),
                "Custom booking chưa có số tiền cần thanh toán.")]);
        }
    }

    public static CustomBookingPaymentPlan ResolvePaymentPlan(
        CustomBooking booking,
        CustomBookingPaymentOption paymentOption,
        decimal? requestedDepositPercent,
        decimal paidAmount)
    {
        var outstandingAmount = booking.TotalAmount - paidAmount;
        if (outstandingAmount <= 0)
        {
            throw new ValidationException([new ValidationFailure("payment",
                "Custom booking này đã thanh toán đủ.")]);
        }

        if (paymentOption == CustomBookingPaymentOption.Full)
        {
            return paidAmount <= 0
                ? new CustomBookingPaymentPlan(
                    FullPurpose,
                    booking.TotalAmount,
                    booking.TotalAmount,
                    0)
                : new CustomBookingPaymentPlan(
                    RemainingPurpose,
                    outstandingAmount,
                    booking.DepositAmount,
                    0);
        }

        if (paidAmount > 0)
        {
            throw new ValidationException([new ValidationFailure("paymentOption",
                "Booking đã có thanh toán đặt cọc. Chọn Full để thanh toán phần còn lại.")]);
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

        return new CustomBookingPaymentPlan(
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

    public static string CreatePaymentDescription(CustomBooking booking) =>
        $"{booking.DepartureDate:yyMMdd}{booking.Id.ToString("N")[^6..].ToUpperInvariant()}";

    public static DateTimeOffset? ResolvePaymentExpiredAt(CustomBooking booking) => null;

    public static bool IsPayOsPayment(Payment payment) =>
        string.Equals(payment.Provider, PayOsProvider, StringComparison.OrdinalIgnoreCase);

    public static bool IsPending(string status) =>
        string.Equals(status, PendingStatus, StringComparison.OrdinalIgnoreCase);

    public static bool IsPaid(string status) =>
        string.Equals(status, PaidStatus, StringComparison.OrdinalIgnoreCase);

    public static decimal GetPaidAmount(CustomBooking booking) =>
        booking.Payments
            .Where(x => IsPayOsPayment(x) && IsPaid(x.PaymentStatus))
            .Sum(x => x.Amount);

    public static void RestorePaymentSummaryFromPaidPayments(CustomBooking booking)
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

    public static Payment? GetLatestPayOsPayment(CustomBooking booking) =>
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
        CustomBooking booking,
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

    public static CreateCustomBookingPaymentResult ToCreatePaymentResult(
        CustomBooking booking,
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
            payment.QrCode);

    public static SyncCustomBookingPaymentResult ToSyncPaymentResult(
        CustomBooking booking,
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
            payment.PaidAt);

    public sealed record CustomBookingPaymentPlan(
        string Purpose,
        decimal Amount,
        decimal DepositAmount,
        decimal RemainingAmount);
}
