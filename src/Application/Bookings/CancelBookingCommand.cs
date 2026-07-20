using FluentValidation.Results;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Application.Points;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.Bookings;

public sealed record CancelBookingCommand(Guid BookingId) : IRequest;

public sealed class CancelBookingCommandValidator : AbstractValidator<CancelBookingCommand>
{
    public CancelBookingCommandValidator()
    {
        RuleFor(x => x.BookingId).NotEmpty();
    }
}

public sealed class CancelBookingCommandHandler : IRequestHandler<CancelBookingCommand>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;
    private readonly TimeProvider _timeProvider;
    private readonly ITripSeatNotifier _tripSeatNotifier;

    public CancelBookingCommandHandler(
        IApplicationDbContext context,
        IUserContext userContext,
        TimeProvider timeProvider,
        ITripSeatNotifier? tripSeatNotifier = null)
    {
        _context = context;
        _userContext = userContext;
        _timeProvider = timeProvider;
        _tripSeatNotifier = tripSeatNotifier ?? NullTripSeatNotifier.Instance;
    }

    public async Task Handle(CancelBookingCommand request, CancellationToken cancellationToken)
    {
        var userId = _userContext.UserId
            ?? throw new ValidationException([]);

        var booking = await _context.Set<Booking>()
            .SingleOrDefaultAsync(
                b => b.Id == request.BookingId && b.BookingType == Booking.SeatBookingType,
                cancellationToken)
            ?? throw new NotFoundException("Booking not found.");

        if (booking.UserId != userId)
            throw new NotFoundException("Booking not found.");

        if (booking.BookingStatus == BookingStatus.Cancelled)
            throw new ValidationException([new ValidationFailure(nameof(request.BookingId), "Booking is already cancelled.")]);

        // Booking khứ hồi: không cho hủy nếu bất kỳ chiều nào đã khởi hành.
        var legTripIds = new List<Guid>();
        if (booking.TripId.HasValue) legTripIds.Add(booking.TripId.Value);
        if (booking.ReturnTripId.HasValue) legTripIds.Add(booking.ReturnTripId.Value);

        if (legTripIds.Count > 0)
        {
            var now = _timeProvider.GetUtcNow();
            var hasDepartedLeg = await _context.Set<Trip>()
                .AnyAsync(t => legTripIds.Contains(t.Id) && t.DepartureTime <= now, cancellationToken);
            if (hasDepartedLeg)
                throw new ValidationException([new ValidationFailure(nameof(request.BookingId),
                    "Cannot cancel a booking after the trip has departed.")]);
        }

        // Lượt khuyến mãi được suy ra từ bookings — đổi status sang Cancelled là tự nhả, không cần bookkeeping.
        booking.BookingStatus = BookingStatus.Cancelled;
        await PointSupport.ReturnRedeemedPointsAsync(
            _context,
            booking,
            $"Hoàn điểm do booking {booking.BookingCode} bị hủy",
            _timeProvider.GetUtcNow(),
            cancellationToken);

        await _context.SaveChangesAsync(cancellationToken);

        await SeatReleaseNotificationSupport.NotifyBookingSeatsReleasedAsync(
            _context, _tripSeatNotifier, booking.Id, cancellationToken);
    }
}
