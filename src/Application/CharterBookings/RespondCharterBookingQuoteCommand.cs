using FluentValidation.Results;
using SaigonWaterbus.Application.Common;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Application.Points;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.CharterBookings;

public sealed record RespondCharterBookingQuoteCommand(
    Guid BookingId,
    CharterBookingQuoteResponseAction Action,
    string? Note = null) : IRequest<CharterBookingDetailDto>;

public sealed class RespondCharterBookingQuoteCommandValidator
    : AbstractValidator<RespondCharterBookingQuoteCommand>
{
    public RespondCharterBookingQuoteCommandValidator()
    {
        RuleFor(x => x.BookingId).NotEmpty();
        RuleFor(x => x.Action).IsInEnum();
        RuleFor(x => x.Note)
            .NotEmpty()
            .When(x => x.Action == CharterBookingQuoteResponseAction.RequestChanges)
            .WithMessage("Nội dung yêu cầu chỉnh sửa là bắt buộc.");
        RuleFor(x => x.Note).MaximumLength(1000).When(x => x.Note is not null);
    }
}

public sealed class RespondCharterBookingQuoteCommandHandler
    : IRequestHandler<RespondCharterBookingQuoteCommand, CharterBookingDetailDto>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;
    private readonly IBoatHoldService _boatHoldService;
    private readonly TimeProvider _timeProvider;
    private readonly ICharterBookingRealtimeNotifier _realtimeNotifier;

    public RespondCharterBookingQuoteCommandHandler(
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

    public async Task<CharterBookingDetailDto> Handle(
        RespondCharterBookingQuoteCommand request,
        CancellationToken cancellationToken)
    {
        var userId = _userContext.UserId
            ?? throw new ValidationException([]);

        var booking = await CharterBookingQuerySupport.BuildBaseQuery(_context)
            .Include(x => x.CharterRoute)
            .Include(x => x.CharterBoats)
            .Include(x => x.Payments)
            .Include(x => x.Tickets)
            .Include(x => x.Promotion)
            .SingleOrDefaultAsync(x => x.Id == request.BookingId, cancellationToken)
            ?? throw new NotFoundException("Charter booking not found.");

        if (booking.UserId != userId)
        {
            throw new NotFoundException("Charter booking not found.");
        }

        if (booking.BookingStatus != BookingStatus.Quoted)
        {
            throw new ValidationException([new ValidationFailure(nameof(request.Action),
                "Chỉ có thể phản hồi khi booking đã được báo giá.")]);
        }

        EnsureNoPaymentLocks(booking);

        if (request.Action == CharterBookingQuoteResponseAction.Accept)
        {
            var now = _timeProvider.GetUtcNow();
            AcceptQuote(booking, now);
            await _context.SaveChangesAsync(cancellationToken);
            await _realtimeNotifier.PublishChangedAsync(
                new CharterBookingRealtimeEvent(
                    booking.Id,
                    "QuoteAccepted",
                    booking.BookingStatus.ToString(),
                    booking.PaymentStatus,
                    now),
                cancellationToken);

            return await LoadDetailAsync(booking.Id, cancellationToken);
        }

        var selectedBoatIds = CharterBookingBoatSelectionSupport.ResolveSelectedBoatIds(booking);
        var releaseRentalUnit = CharterBookingRoutePricingSupport.ResolveRentalUnit(booking);
        var releaseDurationValue = CharterBookingRoutePricingSupport.ResolveRequestedDurationValue(booking);

        switch (request.Action)
        {
            case CharterBookingQuoteResponseAction.RequestChanges:
                await CharterBookingRouteSupport.DeactivateOwnedRouteAsync(
                    _context,
                    booking,
                    cancellationToken);
                RequestChanges(booking, request.Note, _timeProvider.GetUtcNow());
                break;
            case CharterBookingQuoteResponseAction.Reject:
                RejectQuote(booking);
                await CharterBookingRouteSupport.DeactivateOwnedRouteAsync(
                    _context,
                    booking,
                    cancellationToken);
                break;
            default:
                throw new ValidationException([new ValidationFailure(nameof(request.Action),
                    "Hành động phản hồi báo giá không hợp lệ.")]);
        }

        // Báo giá bị từ chối/đổi thì bill cũ không còn — hoàn lại điểm đã giữ cho booking này.
        await PointSupport.ReturnRedeemedPointsAsync(
            _context,
            booking,
            request.Action == CharterBookingQuoteResponseAction.RequestChanges
                ? $"Hoàn điểm do yêu cầu chỉnh sửa báo giá booking {booking.BookingCode}"
                : $"Hoàn điểm do từ chối báo giá booking {booking.BookingCode}",
            _timeProvider.GetUtcNow(),
            cancellationToken);

        await _context.SaveChangesAsync(cancellationToken);
        await ReleaseHeldBoatsAsync(
            booking.Id,
            selectedBoatIds,
            booking.DepartureDate.GetValueOrDefault(),
            booking.StartTime,
            releaseRentalUnit,
            releaseDurationValue,
            cancellationToken);

        var eventName = request.Action == CharterBookingQuoteResponseAction.RequestChanges
            ? "QuoteChangeRequested"
            : "QuoteRejected";
        await _realtimeNotifier.PublishChangedAsync(
            new CharterBookingRealtimeEvent(
                booking.Id,
                eventName,
                booking.BookingStatus.ToString(),
                booking.PaymentStatus,
                _timeProvider.GetUtcNow()),
            cancellationToken);

        return await LoadDetailAsync(booking.Id, cancellationToken);
    }

    private static void AcceptQuote(Booking booking, DateTimeOffset now)
    {
        booking.BookingStatus = BookingStatus.PendingPayment;
        booking.PaymentStatus = "Unpaid";
        booking.DepositAmount = 0;
        booking.RemainingAmount = booking.TotalAmount;
        booking.HoldExpiresAt = now + BookingExpirationPolicy.CharterPaymentCompletionTtl;
    }

    private static void EnsureNoPaymentLocks(Booking booking)
    {
        if (booking.Payments.Any(x =>
                string.Equals(x.PaymentStatus, "Pending", StringComparison.OrdinalIgnoreCase)
                || string.Equals(x.PaymentStatus, "Paid", StringComparison.OrdinalIgnoreCase)))
        {
            throw new ValidationException([new ValidationFailure(nameof(booking.Payments),
                "Booking đã có payment đang chờ hoặc đã thanh toán nên không thể phản hồi báo giá.")]);
        }
    }

    private void RequestChanges(Booking booking, string? note, DateTimeOffset now)
    {
        _context.Set<CharterBookingBoat>().RemoveRange(booking.CharterBoats);
        booking.CharterBoats.Clear();
        booking.BoatId = null;
        booking.Boat = null;
        booking.CharterRouteId = null;
        booking.CharterRoute = null;
        booking.RentalUnit = null;
        booking.DurationValue = null;
        booking.PromotionId = null;
        booking.SubtotalAmount = 0;
        booking.DiscountAmount = 0;
        booking.TotalAmount = 0;
        booking.DepositAmount = 0;
        booking.RemainingAmount = 0;
        booking.PaymentStatus = "Unpaid";
        booking.BookingStatus = BookingStatus.PendingQuote;
        booking.HoldExpiresAt = null;
        booking.SpecialRequests = AppendChangeRequestNote(booking.SpecialRequests, note, now);
    }

    private void RejectQuote(Booking booking)
    {
        booking.BookingStatus = BookingStatus.Cancelled;
        booking.HoldExpiresAt = null;
        foreach (var ticket in booking.Tickets)
        {
            ticket.TicketStatus = TicketStatus.Cancelled;
        }
    }

    private static string? AppendChangeRequestNote(string? existing, string? note, DateTimeOffset now)
    {
        var trimmedNote = note?.Trim();
        if (string.IsNullOrWhiteSpace(trimmedNote))
        {
            return existing;
        }

        var entry = $"Yêu cầu chỉnh sửa báo giá {now:yyyy-MM-dd HH:mm}: {trimmedNote}";
        var value = string.IsNullOrWhiteSpace(existing)
            ? entry
            : $"{existing.Trim()}\n{entry}";

        if (value.Length > 1000)
        {
            throw new ValidationException([new ValidationFailure(nameof(RespondCharterBookingQuoteCommand.Note),
                "Nội dung yêu cầu chỉnh sửa vượt quá 1000 ký tự sau khi ghép với ghi chú hiện tại.")]);
        }

        return value;
    }

    private async Task ReleaseHeldBoatsAsync(
        Guid bookingId,
        IReadOnlyCollection<Guid> boatIds,
        DateOnly departureDate,
        TimeOnly? startTime,
        BoatRentalUnit rentalUnit,
        int durationValue,
        CancellationToken cancellationToken)
    {
        foreach (var boatId in boatIds.Distinct())
        {
            await _boatHoldService.ReleaseAsync(
                bookingId,
                boatId,
                departureDate,
                startTime,
                rentalUnit,
                durationValue,
                cancellationToken);
        }
    }

    private async Task<CharterBookingDetailDto> LoadDetailAsync(Guid bookingId, CancellationToken cancellationToken)
    {
        var booking = await CharterBookingQuerySupport.BuildDetailQuery(_context)
            .AsNoTracking()
            .SingleAsync(x => x.Id == bookingId, cancellationToken);
        var relatedRoutes = await CharterBookingRoutePricingSupport.LoadRelatedRoutesAsync(
            _context,
            booking,
            cancellationToken);

        return CharterBookingQuerySupport.ToDetailDto(booking, relatedRoutes);
    }
}
