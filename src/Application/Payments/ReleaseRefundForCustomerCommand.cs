using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Exceptions;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Constants;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;
using ValidationFailure = FluentValidation.Results.ValidationFailure;

namespace SaigonWaterbus.Application.Payments;

public sealed record ReleaseRefundForCustomerRequest(string? Note = null);

public sealed record ReleaseRefundForCustomerCommand(
    Guid PaymentId,
    string? Note = null)
    : IRequest<PaymentDto>;

public sealed class ReleaseRefundForCustomerCommandValidator : AbstractValidator<ReleaseRefundForCustomerCommand>
{
    public ReleaseRefundForCustomerCommandValidator()
    {
        RuleFor(x => x.PaymentId).NotEmpty();
        RuleFor(x => x.Note).MaximumLength(500).When(x => x.Note is not null);
    }
}

public sealed class ReleaseRefundForCustomerCommandHandler : IRequestHandler<ReleaseRefundForCustomerCommand, PaymentDto>
{
    public const int CustomerRefundRetryWindowDays = 7;

    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;
    private readonly IPaymentNotificationSender _paymentNotificationSender;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ReleaseRefundForCustomerCommandHandler> _logger;

    public ReleaseRefundForCustomerCommandHandler(
        IApplicationDbContext context,
        IUserContext userContext,
        IPaymentNotificationSender paymentNotificationSender,
        TimeProvider timeProvider,
        ILogger<ReleaseRefundForCustomerCommandHandler> logger)
    {
        _context = context;
        _userContext = userContext;
        _paymentNotificationSender = paymentNotificationSender;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<PaymentDto> Handle(ReleaseRefundForCustomerCommand request, CancellationToken cancellationToken)
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
                "Chỉ có thể mở lại hoàn tiền cho payment đã thanh toán.")]);
        }

        // Chỉ cho phép mở lại khi refund đã fail (sau attempt của admin) — không mở lại khi chưa từng refund.
        if (!string.Equals(payment.RefundStatus, PaymentSupport.RefundFailedStatus, StringComparison.OrdinalIgnoreCase))
        {
            throw new ValidationException([new ValidationFailure(nameof(payment.RefundStatus),
                "Chỉ có thể mở lại yêu cầu hoàn tiền sau khi đã thất bại một lần.")]);
        }

        var now = _timeProvider.GetUtcNow();
        var adminId = _userContext.UserId
            ?? throw new InvalidOperationException("Admin user id missing for release-refund handler.");

        // Reset refund state để customer có thể retry 1 lần duy nhất.
        payment.RefundStatus = null;
        payment.RefundFailureReason = null;
        payment.RefundReferenceId = null;
        payment.RefundProcessedByUserId = null;
        payment.RefundedAt = null;

        // Tracking "1 lần duy nhất cho customer".
        payment.CustomerRefundAttempts = 0;
        payment.RefundReleasedAt = now;
        payment.RefundReleasedByUserId = adminId;
        payment.RefundReleasedReason = string.IsNullOrWhiteSpace(request.Note)
            ? null
            : request.Note.Trim();

        await _context.SaveChangesAsync(cancellationToken);

        // Gửi notification cho khách (best-effort — lỗi notification không rollback refund release).
        try
        {
            await _paymentNotificationSender.SendRefundReleasedAsync(
                new RefundReleasedNotification(
                    Email: payment.Booking.ContactEmail ?? string.Empty,
                    ContactName: payment.Booking.ContactName ?? string.Empty,
                    BookingCode: payment.Booking.BookingCode,
                    PaymentCode: payment.PaymentCode,
                    RefundAmount: payment.RefundRequestedAmount ?? payment.Amount,
                    AdminNote: payment.RefundReleasedReason,
                    ReleasedAt: payment.RefundReleasedAt,
                    RetryDeadline: now.AddDays(CustomerRefundRetryWindowDays)),
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Refund-released notification failed but release succeeded. BookingCode: {BookingCode}, PaymentCode: {PaymentCode}",
                payment.Booking.BookingCode,
                payment.PaymentCode);
        }

        return PaymentSupport.ToDto(payment.Booking, payment);
    }
}

