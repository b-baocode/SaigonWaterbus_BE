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

        // Không cho hủy nếu khách đã lên tàu ở bất kỳ chiều nào — mốc là giờ tàu rời BẾN KHÁCH LÊN,
        // không phải giờ rời bến đầu tuyến: khách lên bến giữa vẫn hủy được sau khi tàu đã xuất bến.
        var now = _timeProvider.GetUtcNow();
        var passengerLegs = await _context.Set<BookingPassenger>()
            .Where(p => p.BookingId == booking.Id && p.TripId.HasValue)
            .Select(p => new { TripId = p.TripId!.Value, p.FromStopOrder, p.ToStopOrder })
            .ToListAsync(cancellationToken);

        if (passengerLegs.Count > 0)
        {
            var legTripIds = passengerLegs.Select(p => p.TripId).Distinct().ToList();
            var trips = await _context.Set<Trip>()
                .Include(t => t.TripStops)
                .Include(t => t.Route)
                    .ThenInclude(r => r.RouteStops)
                .Where(t => legTripIds.Contains(t.Id))
                .ToDictionaryAsync(t => t.Id, cancellationToken);

            var hasBoardedLeg = passengerLegs.Any(p =>
                trips.TryGetValue(p.TripId, out var trip)
                && Trips.BookingCutoffSupport.ResolveBoardingTime(trip, p.FromStopOrder, p.ToStopOrder) <= now);
            if (hasBoardedLeg)
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
