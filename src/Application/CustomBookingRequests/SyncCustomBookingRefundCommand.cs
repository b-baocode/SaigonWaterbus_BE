using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Exceptions;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using ForbiddenAccessException = SaigonWaterbus.Application.Common.Exceptions.ForbiddenAccessException;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;

namespace SaigonWaterbus.Application.CustomBookingRequests;

public sealed record SyncCustomBookingRefundCommand(Guid Id)
    : IRequest<CustomBookingRequestDto>;

public sealed class SyncCustomBookingRefundCommandValidator
    : AbstractValidator<SyncCustomBookingRefundCommand>
{
    public SyncCustomBookingRefundCommandValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty()
            .WithMessage("Id yêu cầu thuê tàu không hợp lệ.");
    }
}

public sealed class SyncCustomBookingRefundCommandHandler
    : IRequestHandler<SyncCustomBookingRefundCommand, CustomBookingRequestDto>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;
    private readonly TimeProvider _timeProvider;
    private readonly ICustomBookingPaymentGateway _paymentGateway;

    public SyncCustomBookingRefundCommandHandler(
        IApplicationDbContext context,
        IUserContext userContext,
        TimeProvider timeProvider,
        ICustomBookingPaymentGateway paymentGateway)
    {
        _context = context;
        _userContext = userContext;
        _timeProvider = timeProvider;
        _paymentGateway = paymentGateway;
    }

    public async Task<CustomBookingRequestDto> Handle(
        SyncCustomBookingRefundCommand request,
        CancellationToken cancellationToken)
    {
        var actor = await AuthSupport.GetCurrentUserWithRoleAsync(_context, _userContext, cancellationToken);
        var customRequest = await CustomBookingRequestSupport.IncludeDetails(_context.Set<CustomBookingRequest>())
            .SingleOrDefaultAsync(x => x.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy yêu cầu thuê tàu.");

        var canManage = AuthSupport.IsAdmin(actor);
        var isOwner = AuthSupport.IsCustomer(actor) && customRequest.UserId == actor.Id;
        if (!canManage && !isOwner)
        {
            throw new ForbiddenAccessException();
        }

        if (customRequest.Status is not (CustomBookingRequestStatus.Cancelled or CustomBookingRequestStatus.Confirmed))
        {
            throw AuthSupport.CreateValidationException(
                nameof(customRequest.Status),
                "Chỉ đối soát hoàn tiền cho booking đã hủy hoặc đang chờ xử lý hủy do hoàn tiền lỗi.");
        }

        var quote = customRequest.Quote
            ?? throw AuthSupport.CreateValidationException("quote", "Booking chưa có báo giá để hoàn tiền.");
        if (quote.RefundAmount <= 0)
        {
            throw AuthSupport.CreateValidationException("refundAmount", "Booking không có số tiền đủ điều kiện hoàn.");
        }

        if (string.IsNullOrWhiteSpace(quote.RefundReferenceId))
        {
            throw AuthSupport.CreateValidationException(
                nameof(quote.RefundReferenceId),
                "Booking chưa có mã hoàn tiền PayOS để đối soát.");
        }

        CustomBookingRefundPayoutResult? payout;
        try
        {
            payout = await _paymentGateway.GetRefundPayoutByReferenceIdAsync(
                quote.RefundReferenceId,
                cancellationToken);
        }
        catch (PaymentGatewayException ex)
        {
            throw AuthSupport.CreateValidationException("refund", ex.Message);
        }

        if (payout is null)
        {
            throw AuthSupport.CreateValidationException(
                "refund",
                "PayOS chưa có lệnh chi theo mã hoàn tiền hiện tại.");
        }

        if (!string.IsNullOrWhiteSpace(payout.ReferenceId)
            && !string.Equals(payout.ReferenceId, quote.RefundReferenceId, StringComparison.OrdinalIgnoreCase))
        {
            throw AuthSupport.CreateValidationException("refund", "PayOS trả về mã hoàn tiền không khớp booking.");
        }

        if (payout.Amount.HasValue)
        {
            var expectedAmount = ToPayOsAmount(quote.RefundAmount);
            if (payout.Amount.Value != expectedAmount)
            {
                throw AuthSupport.CreateValidationException(
                    "refundAmount",
                    $"Số tiền hoàn trên PayOS không khớp booking. Expected {expectedAmount}, got {payout.Amount.Value}.");
            }
        }

        quote.RefundPayoutId = string.IsNullOrWhiteSpace(payout.PayoutId)
            ? quote.RefundPayoutId
            : payout.PayoutId;
        quote.RefundStatus = payout.Status;
        quote.RefundFailureReason = null;
        quote.RefundProcessedAt = _timeProvider.GetUtcNow();
        if (!CustomBookingRefundPayoutStatus.IsAccepted(quote))
        {
            quote.RefundFailureReason = CustomBookingRefundPayoutStatus.CreateNotAcceptedReason(
                payout.Status,
                payout.Description);
            await _context.SaveChangesAsync(cancellationToken);
            throw AuthSupport.CreateValidationException("refund", quote.RefundFailureReason);
        }

        if (customRequest.Status != CustomBookingRequestStatus.Cancelled)
        {
            customRequest.Status = CustomBookingRequestStatus.Cancelled;
            customRequest.CancelledAt ??= _timeProvider.GetUtcNow();
            customRequest.CancelledByUserId ??= actor.Id;
        }

        await _context.SaveChangesAsync(cancellationToken);

        var routeSegments = await CustomBookingRequestSupport.GetMatchingRouteSegmentsAsync(
            _context,
            customRequest,
            cancellationToken);
        return CustomBookingRequestDto.From(customRequest, routeSegments);
    }

    private static long ToPayOsAmount(decimal amount)
    {
        if (amount <= 0 || decimal.Truncate(amount) != amount || amount > long.MaxValue)
        {
            throw AuthSupport.CreateValidationException("refundAmount", "Số tiền hoàn phải là số nguyên VND lớn hơn 0.");
        }

        return (long)amount;
    }
}
