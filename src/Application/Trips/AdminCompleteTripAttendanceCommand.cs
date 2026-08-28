using FluentValidation.Results;
using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.CharterBookings;
using SaigonWaterbus.Application.Common.Exceptions;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Application.Points;
using SaigonWaterbus.Application.Tickets;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.Trips;

[Authorize(Roles = "Admin")]
public sealed record AdminCompleteTripAttendanceCommand(Guid TripId)
    : IRequest<AdminCompleteTripAttendanceResult>;

public sealed record AdminCompleteTripAttendanceResult(
    Guid TripId,
    string TripCode,
    int TotalTickets,
    int CheckedInCount,
    int CheckedOutCount,
    int SkippedCount,
    int CompletedBookingCount,
    DateTimeOffset ProcessedAt);

public sealed class AdminCompleteTripAttendanceCommandValidator
    : AbstractValidator<AdminCompleteTripAttendanceCommand>
{
    public AdminCompleteTripAttendanceCommandValidator()
    {
        RuleFor(x => x.TripId).NotEmpty();
    }
}

public sealed class AdminCompleteTripAttendanceCommandHandler
    : IRequestHandler<AdminCompleteTripAttendanceCommand, AdminCompleteTripAttendanceResult>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;
    private readonly TimeProvider _timeProvider;

    public AdminCompleteTripAttendanceCommandHandler(
        IApplicationDbContext context,
        IUserContext userContext,
        TimeProvider? timeProvider = null)
    {
        _context = context;
        _userContext = userContext;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<AdminCompleteTripAttendanceResult> Handle(
        AdminCompleteTripAttendanceCommand request,
        CancellationToken cancellationToken)
    {
        var actor = await AuthSupport.GetCurrentUserWithRoleAsync(_context, _userContext, cancellationToken);
        if (!AuthSupport.IsAdmin(actor))
        {
            throw new ForbiddenAccessException();
        }

        var trip = await _context.Set<Trip>()
            .SingleOrDefaultAsync(x => x.Id == request.TripId, cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy chuyến tàu.");

        var tickets = await _context.Set<Ticket>()
            .Include(x => x.Booking)
            .Include(x => x.BookingPassenger)
            .Where(x => (x.BookingPassenger != null && x.BookingPassenger.TripId == trip.Id)
                || (x.BookingPassenger == null && x.Booking.TripId == trip.Id))
            .ToListAsync(cancellationToken);

        var now = _timeProvider.GetUtcNow();
        var checkedInCount = 0;
        var checkedOutCount = 0;

        foreach (var ticket in tickets.Where(x => x.TicketStatus == TicketStatus.Active))
        {
            ticket.TicketStatus = TicketStatus.CheckedIn;
            ticket.CheckedInAt = now;
            ticket.CheckedInByUserId = actor.Id;
            ticket.CheckedInByUser = actor;
            checkedInCount++;

            await TicketScanHistorySupport.AddEventAsync(
                _context,
                actor,
                TicketScanAction.OverrideCheckIn,
                TicketScanResult.Success,
                new TicketScanRequestMetadata(Source: TicketScanSource.Override, Note: "Admin check-in toàn bộ chuyến."),
                now,
                ticket.TicketCode,
                ticket,
                TicketStatus.Active,
                TicketStatus.CheckedIn,
                null,
                cancellationToken);
        }

        foreach (var ticket in tickets.Where(x => x.TicketStatus == TicketStatus.CheckedIn))
        {
            var statusBefore = ticket.TicketStatus;
            ticket.TicketStatus = TicketStatus.CheckedOut;
            ticket.CheckedOutAt = now;
            ticket.CheckedOutByUserId = actor.Id;
            ticket.CheckedOutByUser = actor;
            checkedOutCount++;

            await TicketScanHistorySupport.AddEventAsync(
                _context,
                actor,
                TicketScanAction.OverrideCheckOut,
                TicketScanResult.Success,
                new TicketScanRequestMetadata(Source: TicketScanSource.Override, Note: "Admin check-out toàn bộ chuyến."),
                now,
                ticket.TicketCode,
                ticket,
                statusBefore,
                TicketStatus.CheckedOut,
                null,
                cancellationToken);
        }

        var bookingIds = tickets.Select(x => x.BookingId).Distinct().ToArray();
        var allTicketsForAffectedBookings = bookingIds.Length == 0
            ? []
            : await _context.Set<Ticket>()
                .Where(x => bookingIds.Contains(x.BookingId))
                .ToListAsync(cancellationToken);
        var completedBookingCount = 0;

        foreach (var booking in tickets.Select(x => x.Booking).DistinctBy(x => x.Id))
        {
            var hasRemainingUsableTicket = allTicketsForAffectedBookings
                .Where(x => x.BookingId == booking.Id)
                .Any(x => x.TicketStatus is not TicketStatus.Cancelled
                    and not TicketStatus.Expired
                    and not TicketStatus.CheckedOut);
            if (hasRemainingUsableTicket)
            {
                continue;
            }

            if (booking.BookingStatus != BookingStatus.Completed)
            {
                booking.BookingStatus = BookingStatus.Completed;
                completedBookingCount++;
            }

            if (Booking.IsCharterBookingType(booking.BookingType))
            {
                await CharterBookingRouteSupport.DeactivateOwnedRouteAsync(_context, booking, cancellationToken);
            }

            await PointSupport.AwardCompletionPointsAsync(_context, booking, now, cancellationToken);
        }

        await _context.SaveChangesAsync(cancellationToken);

        return new AdminCompleteTripAttendanceResult(
            trip.Id,
            trip.TripCode,
            tickets.Count,
            checkedInCount,
            checkedOutCount,
            tickets.Count - checkedOutCount,
            completedBookingCount,
            now);
    }
}
