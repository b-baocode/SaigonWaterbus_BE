using FluentValidation.Results;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Application.Points;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.CharterBookings;

public sealed record CancelCharterBookingCommand(Guid BookingId) : IRequest<CancelCharterBookingResult>;

public sealed class CancelCharterBookingCommandValidator : AbstractValidator<CancelCharterBookingCommand>
{
    public CancelCharterBookingCommandValidator()
    {
        RuleFor(x => x.BookingId).NotEmpty();
    }
}

/// <summary>
/// Kết quả khi customer tự hủy charter booking.
/// - <c>Cancelled</c> = true nếu booking đã chuyển sang Cancelled thành công.
/// - <c>RefundableAmount</c> = tổng tiền còn có thể hoàn theo chính sách (100% / 70% / 0%).
/// - <c>RefundPolicyPercent</c> = % policy đang áp dụng.
/// - <c>RefundPolicyMessage</c> = message tiếng Việt giải thích policy.
/// - <c>RefundablePayments</c> = danh sách payment đã Paid và có thể hoàn (kèm số tiền preview).
/// FE dùng 2 field cuối để hiển thị modal "Yêu cầu hoàn tiền" với OTP + bank info.
/// Lưu ý: API này KHÔNG tự động gọi refund PayOS — hoàn tiền đi qua endpoint /api/payments/{id}/refund
/// vì cần OTP xác thực chủ tài khoản.
/// </summary>
public sealed record CancelCharterBookingResult(
    bool Cancelled,
    decimal RefundableAmount,
    decimal RefundPolicyPercent,
    string RefundPolicyMessage,
    bool CanRequestRefund,
    IReadOnlyList<CharterBookingRefundablePaymentDto> RefundablePayments);

public sealed class CancelCharterBookingCommandHandler : IRequestHandler<CancelCharterBookingCommand, CancelCharterBookingResult>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;
    private readonly TimeProvider _timeProvider;
    private readonly IBoatHoldService _boatHoldService;
    private readonly ICharterBookingRealtimeNotifier _realtimeNotifier;

    public CancelCharterBookingCommandHandler(
        IApplicationDbContext context,
        IUserContext userContext,
        TimeProvider timeProvider,
        IBoatHoldService? boatHoldService = null,
        ICharterBookingRealtimeNotifier? realtimeNotifier = null)
    {
        _context = context;
        _userContext = userContext;
        _timeProvider = timeProvider;
        _boatHoldService = boatHoldService ?? NullBoatHoldService.Instance;
        _realtimeNotifier = realtimeNotifier ?? NullCharterBookingRealtimeNotifier.Instance;
    }

    public async Task<CancelCharterBookingResult> Handle(CancelCharterBookingCommand request, CancellationToken cancellationToken)
    {
        var userId = _userContext.UserId
            ?? throw new ValidationException([]);

        var booking = await CharterBookingQuerySupport.BuildBaseQuery(_context)
            .Include(x => x.CharterBoats)
            .Include(x => x.Tickets)
            .Include(x => x.Promotion)
            .Include(x => x.Payments)
            .SingleOrDefaultAsync(b => b.Id == request.BookingId, cancellationToken)
            ?? throw new NotFoundException("Charter booking not found.");

        if (booking.UserId != userId)
            throw new NotFoundException("Charter booking not found.");

        if (booking.BookingStatus == BookingStatus.Cancelled)
            throw new ValidationException([new ValidationFailure(nameof(request.BookingId),
                "Yêu cầu thuê tàu đã được hủy trước đó.")]);

        if (booking.BookingStatus == BookingStatus.Completed)
            throw new ValidationException([new ValidationFailure(nameof(request.BookingId),
                "Không thể hủy yêu cầu thuê tàu đã hoàn tất.")]);

        booking.BookingStatus = BookingStatus.Cancelled;
        foreach (var ticket in booking.Tickets)
        {
            ticket.TicketStatus = TicketStatus.Cancelled;
        }

        await PointSupport.ReturnRedeemedPointsAsync(
            _context,
            booking,
            $"Hoàn điểm do charter booking {booking.BookingCode} bị hủy",
            _timeProvider.GetUtcNow(),
            cancellationToken);

        await CharterBookingTripSupport.CancelLinkedTripsAsync(
            _context,
            booking.Id,
            $"Charter booking {booking.BookingCode} đã bị hủy.",
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

        var now = _timeProvider.GetUtcNow();
        var summary = CharterBookingRefundSupport.BuildSummary(booking, now);
        var refundablePayments = CharterBookingRefundSupport
            .GetRefundablePayments(booking, summary)
            .Select(x => new CharterBookingRefundablePaymentDto(
                x.PaymentId,
                x.PaymentCode,
                x.PaidAmount,
                x.AlreadyRefundedAmount,
                x.AvailableRefundAmount,
                x.PaymentStatus))
            .ToList();

        return new CancelCharterBookingResult(
            Cancelled: true,
            RefundableAmount: summary.OutstandingRefundAmount,
            RefundPolicyPercent: summary.PolicyPercent,
            RefundPolicyMessage: summary.PolicyMessage,
            CanRequestRefund: summary.CanRequestRefund,
            RefundablePayments: refundablePayments);
    }
}
