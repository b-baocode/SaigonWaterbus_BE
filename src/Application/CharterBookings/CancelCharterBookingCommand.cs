using FluentValidation.Results;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.CharterBookings;

public sealed record CancelCharterBookingCommand(Guid BookingId) : IRequest;

public sealed class CancelCharterBookingCommandValidator : AbstractValidator<CancelCharterBookingCommand>
{
    public CancelCharterBookingCommandValidator()
    {
        RuleFor(x => x.BookingId).NotEmpty();
    }
}

public sealed class CancelCharterBookingCommandHandler : IRequestHandler<CancelCharterBookingCommand>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;
    private readonly IBoatHoldService _boatHoldService;
    private readonly ICharterBookingRealtimeNotifier _realtimeNotifier;

    public CancelCharterBookingCommandHandler(
        IApplicationDbContext context,
        IUserContext userContext,
        IBoatHoldService? boatHoldService = null,
        ICharterBookingRealtimeNotifier? realtimeNotifier = null)
    {
        _context = context;
        _userContext = userContext;
        _boatHoldService = boatHoldService ?? NullBoatHoldService.Instance;
        _realtimeNotifier = realtimeNotifier ?? NullCharterBookingRealtimeNotifier.Instance;
    }

    public async Task Handle(CancelCharterBookingCommand request, CancellationToken cancellationToken)
    {
        var userId = _userContext.UserId
            ?? throw new ValidationException([]);

        var booking = await CharterBookingQuerySupport.BuildBaseQuery(_context)
            .Include(x => x.CharterBoats)
            .Include(x => x.Tickets)
            .Include(x => x.Promotion)
            .SingleOrDefaultAsync(b => b.Id == request.BookingId, cancellationToken)
            ?? throw new NotFoundException("Charter booking not found.");

        if (booking.UserId != userId)
            throw new NotFoundException("Charter booking not found.");

        if (booking.BookingStatus == BookingStatus.Cancelled)
            throw new ValidationException([new ValidationFailure(nameof(request.BookingId),
                "Yêu cầu thuê tàu đã được hủy trước đó.")]);

        if (booking.BookingStatus is BookingStatus.Completed or BookingStatus.Refunded)
            throw new ValidationException([new ValidationFailure(nameof(request.BookingId),
                "Không thể hủy yêu cầu thuê tàu đã hoàn tất.")]);

        booking.BookingStatus = BookingStatus.Cancelled;
        foreach (var ticket in booking.Tickets)
        {
            ticket.TicketStatus = TicketStatus.Cancelled;
        }

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
    }
}
