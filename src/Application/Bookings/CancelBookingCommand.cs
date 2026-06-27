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

    public CancelBookingCommandHandler(
        IApplicationDbContext context, IUserContext userContext, TimeProvider timeProvider)
    {
        _context = context;
        _userContext = userContext;
        _timeProvider = timeProvider;
    }

    public async Task Handle(CancelBookingCommand request, CancellationToken cancellationToken)
    {
        var userId = _userContext.UserId
            ?? throw new ValidationException([]);

        var booking = await _context.Set<Booking>()
            .Include(b => b.Promotion)
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

        booking.BookingStatus = BookingStatus.Cancelled;

        if (booking.PromotionId.HasValue && booking.Promotion is not null)
            booking.Promotion.UsageCount = Math.Max(0, booking.Promotion.UsageCount - 1);

        await _context.SaveChangesAsync(cancellationToken);
    }
}
