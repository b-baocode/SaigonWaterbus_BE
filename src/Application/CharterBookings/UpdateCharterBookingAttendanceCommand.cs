using FluentValidation.Results;
using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Exceptions;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Application.Points;
using SaigonWaterbus.Application.Tickets;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.CharterBookings;

public sealed record UpdateCharterBookingAttendanceCommand(
    string QrToken,
    CharterBookingAttendanceAction Action,
    CharterBookingAttendanceMode Mode,
    IReadOnlyList<Guid>? TicketIds,
    TicketScanRequestMetadata? Metadata = null)
    : IRequest<CharterBookingAttendanceResult>;

public sealed class UpdateCharterBookingAttendanceCommandValidator
    : AbstractValidator<UpdateCharterBookingAttendanceCommand>
{
    public UpdateCharterBookingAttendanceCommandValidator()
    {
        RuleFor(x => x.QrToken).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Action).IsInEnum();
        RuleFor(x => x.Mode).IsInEnum();
        RuleFor(x => x.TicketIds)
            .NotEmpty()
            .When(x => x.Mode == CharterBookingAttendanceMode.Selected)
            .WithMessage("ticketIds is required when mode is Selected.");
        RuleForEach(x => x.TicketIds)
            .NotEmpty()
            .When(x => x.TicketIds is not null);
    }
}

public sealed class UpdateCharterBookingAttendanceCommandHandler
    : IRequestHandler<UpdateCharterBookingAttendanceCommand, CharterBookingAttendanceResult>
{
    private const string PaidBookingPaymentStatus = BookingPaymentStatusExtensions.PaidValue;

    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;
    private readonly TimeProvider _timeProvider;
    private readonly ICharterBookingRealtimeNotifier _realtimeNotifier;

    public UpdateCharterBookingAttendanceCommandHandler(
        IApplicationDbContext context,
        IUserContext userContext,
        TimeProvider timeProvider,
        ICharterBookingRealtimeNotifier? realtimeNotifier = null)
    {
        _context = context;
        _userContext = userContext;
        _timeProvider = timeProvider;
        _realtimeNotifier = realtimeNotifier ?? NullCharterBookingRealtimeNotifier.Instance;
    }

    public async Task<CharterBookingAttendanceResult> Handle(
        UpdateCharterBookingAttendanceCommand request,
        CancellationToken cancellationToken)
    {
        var currentUser = await AuthSupport.GetCurrentUserWithRoleAsync(_context, _userContext, cancellationToken);
        var qrToken = request.QrToken.Trim();
        var booking = await BuildBookingQuery()
            .SingleOrDefaultAsync(
                x => x.CharterBookingQrToken == qrToken,
                cancellationToken)
            ?? throw new NotFoundException("Charter booking not found.");

        var now = _timeProvider.GetUtcNow();
        var metadata = request.Metadata ?? new TicketScanRequestMetadata();
        if (AuthSupport.IsStaff(currentUser))
        {
            await TicketStaffScanAuthorizationSupport.EnsureStaffCanOperateCharterBookingAsync(
                _context, currentUser, booking, now, cancellationToken);
        }
        else
        {
            await CharterBookingAssignmentSupport.EnsureCanViewOperationalAsync(
                _context,
                currentUser,
                booking,
                includeCustomerOwner: false,
                notFoundWhenDenied: false,
                cancellationToken);
        }

        EnsureBookingCanUpdateAttendance(booking, request.Action);

        var tickets = SelectTickets(booking, request);
        if (tickets.Count == 0)
        {
            throw new ValidationException([new ValidationFailure("tickets",
                "Charter booking chua co ve hanh khach de cap nhat.")]);
        }

        var skippedTickets = new List<CharterBookingAttendanceSkippedTicketDto>();
        var updatedTickets = new List<Ticket>();
        var updatedCount = 0;

        foreach (var ticket in tickets)
        {
            var reason = GetSkipReason(ticket, request.Action);
            if (reason is not null)
            {
                skippedTickets.Add(ToSkippedTicketDto(ticket, reason));
                continue;
            }

            await TicketScanHistorySupport.EnsureTripStopBelongsToTicketAsync(
                _context, metadata, ticket, cancellationToken);
            if (request.Action == CharterBookingAttendanceAction.CheckIn)
            {
                TicketAttendanceWindowSupport.EnsureCanCheckInAt(ticket, booking, now);
            }
            else
            {
                TicketAttendanceWindowSupport.EnsureCanCheckOutAt(ticket, booking, now);
            }

            if (request.Action == CharterBookingAttendanceAction.CheckIn)
            {
                ticket.TicketStatus = TicketStatus.CheckedIn;
                ticket.CheckedInAt = now;
                ticket.CheckedInByUserId = currentUser.Id;
                ticket.CheckedInByUser = currentUser;
            }
            else
            {
                ticket.TicketStatus = TicketStatus.CheckedOut;
                ticket.CheckedOutAt = now;
                ticket.CheckedOutByUserId = currentUser.Id;
                ticket.CheckedOutByUser = currentUser;
            }

            updatedTickets.Add(ticket);
            updatedCount++;
        }

        if (request.Action == CharterBookingAttendanceAction.CheckOut)
        {
            await CompleteBookingIfAllTicketsCheckedOutAsync(booking, now, cancellationToken);
        }

        if (updatedTickets.Count > 0)
        {
            var isCheckIn = request.Action == CharterBookingAttendanceAction.CheckIn;
            await TicketScanHistorySupport.AddSuccessfulBatchEventsAsync(
                _context,
                currentUser,
                isCheckIn ? TicketScanAction.CheckIn : TicketScanAction.CheckOut,
                metadata,
                now,
                qrToken,
                updatedTickets,
                isCheckIn ? TicketStatus.Active : TicketStatus.CheckedIn,
                isCheckIn ? TicketStatus.CheckedIn : TicketStatus.CheckedOut,
                cancellationToken);
        }

        await _context.SaveChangesAsync(cancellationToken);
        await _realtimeNotifier.PublishChangedAsync(
            new CharterBookingRealtimeEvent(
                booking.Id,
                request.Action.ToString(),
                booking.BookingStatus.ToString(),
                booking.PaymentStatus,
                now),
            cancellationToken);

        return new CharterBookingAttendanceResult(
            request.Action,
            request.Mode,
            tickets.Count,
            updatedCount,
            skippedTickets.Count,
            skippedTickets,
            CharterBookingManifestSupport.ToDto(booking, now));
    }

    private IQueryable<Booking> BuildBookingQuery() =>
        CharterBookingQuerySupport.BuildBaseQuery(_context)
            .Include(x => x.Boat)
            .Include(x => x.CharterRoute)
            .Include(x => x.CharterBoats)
            .Include(x => x.FromStation)
            .Include(x => x.ToStation)
            .Include(x => x.Trip)
                .ThenInclude(x => x!.TripStops)
            .Include(x => x.ItineraryStops)
                .ThenInclude(x => x.Station)
            .Include(x => x.Passengers)
            .Include(x => x.Tickets)
                .ThenInclude(x => x.BookingPassenger)
            .Include(x => x.Tickets)
                .ThenInclude(x => x.CheckedInByUser)
            .Include(x => x.Tickets)
                .ThenInclude(x => x.CheckedOutByUser);

    private static void EnsureBookingCanUpdateAttendance(
        Booking booking,
        CharterBookingAttendanceAction action)
    {
        if (booking.BookingStatus != BookingStatus.Confirmed)
        {
            throw new ValidationException([new ValidationFailure("booking",
                action == CharterBookingAttendanceAction.CheckIn
                    ? "Booking chua san sang de check-in."
                    : "Booking khong san sang de check-out.")]);
        }

        if (action == CharterBookingAttendanceAction.CheckIn
            && !string.Equals(booking.PaymentStatus, PaidBookingPaymentStatus, StringComparison.OrdinalIgnoreCase)
            && booking.RemainingAmount > 0)
        {
            throw new ValidationException([new ValidationFailure("payment",
                "Booking chua thanh toan du de check-in.")]);
        }
    }

    private static IReadOnlyList<Ticket> SelectTickets(
        Booking booking,
        UpdateCharterBookingAttendanceCommand request)
    {
        var passengerTickets = booking.Tickets
            .Where(x => x.BookingPassengerId.HasValue)
            .OrderBy(x => x.BookingPassenger?.FullName)
            .ThenBy(x => x.TicketCode)
            .ToList();

        if (request.Mode == CharterBookingAttendanceMode.All)
        {
            return passengerTickets;
        }

        var selectedTicketIds = request.TicketIds!.Distinct().ToArray();
        var ticketsById = passengerTickets.ToDictionary(x => x.Id);
        var missingTicketIds = selectedTicketIds
            .Where(x => !ticketsById.ContainsKey(x))
            .ToArray();

        if (missingTicketIds.Length > 0)
        {
            throw new ValidationException([new ValidationFailure(nameof(request.TicketIds),
                "Danh sach ticketIds co ve khong thuoc charter booking hoac khong phai ve hanh khach.")]);
        }

        return selectedTicketIds
            .Select(x => ticketsById[x])
            .ToList();
    }

    private static string? GetSkipReason(
        Ticket ticket,
        CharterBookingAttendanceAction action)
    {
        if (ticket.TicketStatus is TicketStatus.Cancelled or TicketStatus.Expired)
        {
            return "Ve khong con hieu luc.";
        }

        if (action == CharterBookingAttendanceAction.CheckIn)
        {
            return ticket.TicketStatus switch
            {
                TicketStatus.Active => null,
                TicketStatus.CheckedIn => "Ve da check-in.",
                TicketStatus.CheckedOut => "Ve da check-out.",
                _ => "Ve khong the check-in."
            };
        }

        if (ticket.TicketStatus == TicketStatus.CheckedOut || ticket.CheckedOutAt.HasValue)
        {
            return "Ve da check-out.";
        }

        if (ticket.TicketStatus != TicketStatus.CheckedIn || !ticket.CheckedInAt.HasValue)
        {
            return "Ve chua check-in nen chua the check-out.";
        }

        return null;
    }

    private async Task CompleteBookingIfAllTicketsCheckedOutAsync(
        Booking booking,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var hasRemainingUsableTicket = booking.Tickets.Any(x =>
            x.TicketStatus != TicketStatus.Cancelled
            && x.TicketStatus != TicketStatus.Expired
            && x.TicketStatus != TicketStatus.CheckedOut);

        if (!hasRemainingUsableTicket)
        {
            booking.BookingStatus = BookingStatus.Completed;
            await CharterBookingRouteSupport.DeactivateOwnedRouteAsync(
                _context,
                booking,
                cancellationToken);
            // Khách đã xuống tàu hết → dịch vụ hoàn tất, giờ mới tích điểm.
            await PointSupport.AwardCompletionPointsAsync(
                _context,
                booking,
                now,
                cancellationToken);
        }
    }

    private static CharterBookingAttendanceSkippedTicketDto ToSkippedTicketDto(
        Ticket ticket,
        string reason) =>
        new(
            ticket.Id,
            ticket.TicketCode,
            ticket.BookingPassengerId,
            ticket.BookingPassenger?.FullName,
            ticket.TicketStatus.ToString(),
            reason);
}
