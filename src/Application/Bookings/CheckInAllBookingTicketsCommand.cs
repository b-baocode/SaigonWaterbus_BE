using FluentValidation.Results;
using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Exceptions;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.Bookings;

/// <summary>
/// Staff quét QR chung của booking thường → check-in toàn bộ vé Active một lượt.
/// </summary>
public sealed record CheckInAllBookingTicketsCommand(string QrToken) : IRequest<BookingManifestDto>;

public sealed class CheckInAllBookingTicketsCommandValidator : AbstractValidator<CheckInAllBookingTicketsCommand>
{
    public CheckInAllBookingTicketsCommandValidator()
    {
        RuleFor(x => x.QrToken).NotEmpty().MaximumLength(100);
    }
}

public sealed class CheckInAllBookingTicketsCommandHandler
    : IRequestHandler<CheckInAllBookingTicketsCommand, BookingManifestDto>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;
    private readonly TimeProvider _timeProvider;

    public CheckInAllBookingTicketsCommandHandler(
        IApplicationDbContext context,
        IUserContext userContext,
        TimeProvider timeProvider)
    {
        _context = context;
        _userContext = userContext;
        _timeProvider = timeProvider;
    }

    public async Task<BookingManifestDto> Handle(
        CheckInAllBookingTicketsCommand request,
        CancellationToken cancellationToken)
    {
        var currentUser = await AuthSupport.GetCurrentUserWithRoleAsync(_context, _userContext, cancellationToken);
        if (!AuthSupport.IsAdmin(currentUser)
            && !AuthSupport.IsManager(currentUser)
            && !AuthSupport.IsStaff(currentUser))
        {
            throw new ForbiddenAccessException();
        }

        var qrToken = request.QrToken.Trim();
        var booking = await _context.Set<Booking>()
            .Include(x => x.Tickets)
            .SingleOrDefaultAsync(
                x => x.CharterBookingQrToken == qrToken && x.BookingType == Booking.SeatBookingType,
                cancellationToken)
            ?? throw new NotFoundException("Booking not found.");

        if (!BookingManifestSupport.CanCheckInBooking(booking))
        {
            throw new ValidationException([new ValidationFailure("booking",
                "Booking chưa sẵn sàng để check-in (chưa xác nhận hoặc chưa thanh toán đủ).")]);
        }

        var activeTickets = booking.Tickets
            .Where(x => x.TicketStatus == TicketStatus.Active)
            .ToList();
        if (activeTickets.Count == 0)
        {
            throw new ValidationException([new ValidationFailure("booking",
                "Không còn vé nào ở trạng thái Active để check-in.")]);
        }

        var now = _timeProvider.GetUtcNow();
        foreach (var ticket in activeTickets)
        {
            ticket.TicketStatus = TicketStatus.CheckedIn;
            ticket.CheckedInAt = now;
            ticket.CheckedInByUserId = currentUser.Id;
        }

        await _context.SaveChangesAsync(cancellationToken);

        var refreshed = await BookingManifestSupport.BuildQuery(_context)
            .SingleAsync(x => x.Id == booking.Id, cancellationToken);
        return BookingManifestSupport.ToDto(refreshed);
    }
}
