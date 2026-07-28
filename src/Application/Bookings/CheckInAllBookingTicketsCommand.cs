using FluentValidation.Results;
using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Exceptions;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Application.Tickets;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.Bookings;

/// <summary>
/// Staff quét QR chung của booking thường → check-in toàn bộ vé Active một lượt.
/// Booking khứ hồi: truyền TripCode của chuyến đang boarding để chỉ check-in vé chiều đó;
/// bỏ trống thì check-in tất cả (hành vi cũ, phù hợp booking một chiều).
/// </summary>
public sealed record CheckInAllBookingTicketsCommand(string QrToken, string? TripCode = null)
    : IRequest<BookingManifestDto>;

public sealed class CheckInAllBookingTicketsCommandValidator : AbstractValidator<CheckInAllBookingTicketsCommand>
{
    public CheckInAllBookingTicketsCommandValidator()
    {
        RuleFor(x => x.QrToken).NotEmpty().MaximumLength(100);
        RuleFor(x => x.TripCode).MaximumLength(50);
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
                .ThenInclude(x => x.BookingPassenger)
            .Include(x => x.Trip)
            .Include(x => x.ReturnTrip)
            .SingleOrDefaultAsync(
                x => x.CharterBookingQrToken == qrToken && x.BookingType == Booking.SeatBookingType,
                cancellationToken)
            ?? throw new NotFoundException("Booking not found.");

        if (!BookingManifestSupport.CanCheckInBooking(booking))
        {
            throw new ValidationException([new ValidationFailure("booking",
                "Booking chưa sẵn sàng để check-in (chưa xác nhận hoặc chưa thanh toán đủ).")]);
        }

        // Booking khứ hồi: lọc vé theo chuyến đang boarding khi staff truyền tripCode.
        Guid? legTripId = null;
        if (!string.IsNullOrWhiteSpace(request.TripCode))
        {
            var tripCode = request.TripCode.Trim().ToUpperInvariant();
            if (string.Equals(booking.Trip?.TripCode, tripCode, StringComparison.OrdinalIgnoreCase))
            {
                legTripId = booking.TripId;
            }
            else if (string.Equals(booking.ReturnTrip?.TripCode, tripCode, StringComparison.OrdinalIgnoreCase))
            {
                legTripId = booking.ReturnTripId;
            }
            else
            {
                throw new ValidationException([new ValidationFailure(nameof(request.TripCode),
                    "Trip không thuộc booking này.")]);
            }
        }

        var activeTickets = booking.Tickets
            .Where(x => x.TicketStatus == TicketStatus.Active)
            .Where(x => x.BookingPassenger is null
                || LapInfantTicketSupport.RequiresOwnTicket(x.BookingPassenger))
            .Where(x => legTripId is null
                || x.BookingPassenger?.TripId is null
                || x.BookingPassenger.TripId == legTripId)
            .ToList();
        if (activeTickets.Count == 0)
        {
            throw new ValidationException([new ValidationFailure("booking",
                "Không còn vé nào ở trạng thái Active để check-in.")]);
        }

        var now = _timeProvider.GetUtcNow();
        foreach (var trip in ResolveTargetTrips(booking, activeTickets))
        {
            await TicketStaffScanAuthorizationSupport.EnsureStaffCanOperateTripAsync(
                _context, currentUser, trip, now, cancellationToken);
        }

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

    private static IReadOnlyList<Trip> ResolveTargetTrips(
        Booking booking,
        IReadOnlyList<Ticket> tickets)
    {
        var trips = new Dictionary<Guid, Trip>();
        foreach (var ticket in tickets)
        {
            var tripId = ticket.BookingPassenger?.TripId ?? booking.TripId;
            if (tripId == booking.TripId && booking.Trip is not null)
            {
                trips[booking.Trip.Id] = booking.Trip;
            }
            else if (tripId == booking.ReturnTripId && booking.ReturnTrip is not null)
            {
                trips[booking.ReturnTrip.Id] = booking.ReturnTrip;
            }
        }

        return trips.Values.ToList();
    }
}
