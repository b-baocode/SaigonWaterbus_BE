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

public sealed record CreateCharterPassengerAddInsurancePaymentCommand(
    Guid BookingId)
    : IRequest<CreateCharterBookingPaymentResult>;

public sealed class CreateCharterPassengerAddInsurancePaymentCommandValidator
    : AbstractValidator<CreateCharterPassengerAddInsurancePaymentCommand>
{
    public CreateCharterPassengerAddInsurancePaymentCommandValidator()
    {
        RuleFor(x => x.BookingId).NotEmpty();
    }
}

public sealed class CreateCharterPassengerAddInsurancePaymentCommandHandler
    : IRequestHandler<CreateCharterPassengerAddInsurancePaymentCommand, CreateCharterBookingPaymentResult>
{
    private const string PassengerAddInsurancePurpose = "PassengerAddInsurance";

    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;
    private readonly ICharterBookingPaymentGateway _paymentGateway;
    private readonly TimeProvider _timeProvider;
    private readonly ICharterBookingRealtimeNotifier _realtimeNotifier;

    public CreateCharterPassengerAddInsurancePaymentCommandHandler(
        IApplicationDbContext context,
        IUserContext userContext,
        ICharterBookingPaymentGateway paymentGateway,
        TimeProvider timeProvider,
        ICharterBookingRealtimeNotifier? realtimeNotifier = null)
    {
        _context = context;
        _userContext = userContext;
        _paymentGateway = paymentGateway;
        _timeProvider = timeProvider;
        _realtimeNotifier = realtimeNotifier ?? NullCharterBookingRealtimeNotifier.Instance;
    }

    public async Task<CreateCharterBookingPaymentResult> Handle(
        CreateCharterPassengerAddInsurancePaymentCommand request,
        CancellationToken cancellationToken)
    {
        var booking = await CharterBookingPaymentSupport.GetOwnedCharterBookingAsync(
            _context,
            _userContext,
            request.BookingId,
            includePayments: true,
            cancellationToken);

        if (booking.BookingStatus != BookingStatus.AwaitingPayment)
        {
            throw new ValidationException([new ValidationFailure(nameof(booking.BookingStatus),
                "Chỉ tạo thanh toán bảo hiểm bổ sung khi booking đang ở trạng thái chờ thanh toán BH cho hành khách mới.")]);
        }

        var now = _timeProvider.GetUtcNow();

        if (booking.HoldExpiresAt.HasValue && booking.HoldExpiresAt.Value <= now)
        {
            throw new ValidationException([new ValidationFailure(nameof(booking.HoldExpiresAt),
                "Thời hạn thanh toán bảo hiểm bổ sung đã hết. Vui lòng liên hệ admin để xử lý.")]);
        }

        if (booking.RemainingAmount <= 0)
        {
            throw new ValidationException([new ValidationFailure(nameof(booking.RemainingAmount),
                "Charter booking này không có phần bảo hiểm bổ sung cần thanh toán.")]);
        }

        var existingPendingPayment = booking.Payments
            .Where(CharterBookingPaymentSupport.IsPayOsPayment)
            .OrderByDescending(x => x.Created)
            .FirstOrDefault(x =>
                CharterBookingPaymentSupport.IsPending(x.PaymentStatus)
                && !PaymentSupport.IsExpired(x, now)
                && string.Equals(x.PaymentPurpose, PassengerAddInsurancePurpose, StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(x.CheckoutUrl));
        if (existingPendingPayment is not null)
        {
            return CharterBookingPaymentSupport.ToCreatePaymentResult(booking, existingPendingPayment);
        }

        var orderCode = await CharterBookingPaymentSupport.GeneratePaymentOrderCodeAsync(
            _context,
            now,
            cancellationToken);
        var amount = CharterBookingPaymentSupport.ToPayOsAmount(
            booking.RemainingAmount,
            nameof(booking.RemainingAmount),
            "Số tiền bảo hiểm bổ sung phải là số nguyên VND lớn hơn 0.");
        var expiresAt = PaymentSupport.ResolvePaymentExpiresAt(now);

        var payment = new Payment
        {
            BookingId = booking.Id,
            PaymentCode = orderCode.ToString(CultureInfo.InvariantCulture),
            Provider = CharterBookingPaymentSupport.PayOsProvider,
            Amount = booking.RemainingAmount,
            Currency = booking.Currency,
            PaymentMethod = CharterBookingPaymentSupport.PayOsProvider,
            PaymentPurpose = PassengerAddInsurancePurpose,
            PaymentStatus = CharterBookingPaymentSupport.PendingStatus,
            ExpiresAt = expiresAt
        };
        booking.Payments.Add(payment);
        _context.Set<Payment>().Add(payment);
        await _context.SaveChangesAsync(cancellationToken);

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
                    $"{PassengerAddInsurancePurpose} charter booking {booking.BookingCode}",
                    expiresAt),
                cancellationToken);
        }
        catch (PaymentGatewayException ex)
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
            throw new ValidationException([new ValidationFailure("payment", ex.Message)]);
        }

        payment.QrCode = paymentResult.QrCode;
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

        return CharterBookingPaymentSupport.ToCreatePaymentResult(booking, payment);
    }
}
