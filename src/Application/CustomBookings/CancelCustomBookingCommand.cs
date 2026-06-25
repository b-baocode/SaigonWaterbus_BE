using FluentValidation.Results;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.CustomBookings;

public sealed record CancelCustomBookingCommand(Guid BookingId) : IRequest;

public sealed class CancelCustomBookingCommandValidator : AbstractValidator<CancelCustomBookingCommand>
{
    public CancelCustomBookingCommandValidator()
    {
        RuleFor(x => x.BookingId).NotEmpty();
    }
}

public sealed class CancelCustomBookingCommandHandler : IRequestHandler<CancelCustomBookingCommand>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;

    public CancelCustomBookingCommandHandler(IApplicationDbContext context, IUserContext userContext)
    {
        _context = context;
        _userContext = userContext;
    }

    public async Task Handle(CancelCustomBookingCommand request, CancellationToken cancellationToken)
    {
        var userId = _userContext.UserId
            ?? throw new ValidationException([]);

        var booking = await _context.Set<CustomBooking>()
            .Include(x => x.Tickets)
            .SingleOrDefaultAsync(b => b.Id == request.BookingId, cancellationToken)
            ?? throw new NotFoundException("Custom booking not found.");

        if (booking.UserId != userId)
            throw new NotFoundException("Custom booking not found.");

        if (booking.BookingStatus == BookingStatus.Cancelled)
            throw new ValidationException([new ValidationFailure(nameof(request.BookingId),
                "Yêu cầu thuê tàu đã được hủy trước đó.")]);

        if (booking.BookingStatus is BookingStatus.Completed or BookingStatus.Refunded)
            throw new ValidationException([new ValidationFailure(nameof(request.BookingId),
                "Không thể hủy yêu cầu thuê tàu đã hoàn tất.")]);

        booking.BookingStatus = BookingStatus.Cancelled;
        foreach (var ticket in booking.Tickets)
        {
            ticket.TicketStatus = BookingTicketStatus.Cancelled;
        }

        await _context.SaveChangesAsync(cancellationToken);
    }
}
