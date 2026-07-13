using FluentValidation.Results;
using SaigonWaterbus.Application.Common.Interfaces;
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

        if (booking.TripId.HasValue)
        {
            var trip = await _context.Set<Trip>()
                .SingleOrDefaultAsync(t => t.Id == booking.TripId.Value, cancellationToken);

            var now = _timeProvider.GetUtcNow();
            if (trip is not null && trip.DepartureTime <= now)
                throw new ValidationException([new ValidationFailure(nameof(request.BookingId),
                    "Cannot cancel a booking after the trip has departed.")]);
        }

        // Lượt khuyến mãi được suy ra từ bookings — đổi status sang Cancelled là tự nhả, không cần bookkeeping.
        booking.BookingStatus = BookingStatus.Cancelled;

        await _context.SaveChangesAsync(cancellationToken);

        if (booking.TripId.HasValue)
        {
            var seatCodes = await _context.Set<BookingPassenger>()
                .Where(p => p.BookingId == booking.Id && p.TripSeatId.HasValue)
                .Select(p => p.TripSeat!.Seat.Code)
                .ToListAsync(cancellationToken);
            if (seatCodes.Count > 0)
            {
                await _tripSeatNotifier.PublishSeatStatusChangedAsync(
                    booking.TripId.Value,
                    seatCodes.Distinct().Select(code => new TripSeatStatusChange(code, "Available")).ToList(),
                    cancellationToken);
            }
        }
    }
}
