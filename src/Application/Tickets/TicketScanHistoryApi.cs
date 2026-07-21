using FluentValidation.Results;
using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Exceptions;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Application.StaffWorkAssignments;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.Tickets;

public sealed record TicketScanEventDto(
    Guid EventId,
    Guid? TicketId,
    string? TicketCode,
    Guid? BookingId,
    string? BookingCode,
    Guid? TripId,
    string? TripCode,
    Guid PerformedByUserId,
    string PerformedByName,
    Guid? StaffWorkAssignmentId,
    string? AssignmentType,
    Guid? BoatId,
    string? BoatCode,
    string? BoatName,
    Guid? StationId,
    string? StationCode,
    string? StationName,
    Guid? TripStopId,
    int? StopOrder,
    string Action,
    string Result,
    string Source,
    string? FailureReason,
    string? ClientOperationId,
    DateTimeOffset? DeviceTime,
    DateTimeOffset ServerTime,
    string? Note,
    string? ScannedCodeOrToken,
    string? TicketStatusBefore,
    string? TicketStatusAfter);

public sealed record TicketScanRequestMetadata(
    TicketScanSource Source = TicketScanSource.Qr,
    Guid? TripStopId = null,
    string? ClientOperationId = null,
    DateTimeOffset? DeviceTime = null,
    string? Note = null);

[Authorize(Roles = "Staff")]
public sealed record GetMyTicketScanHistoryQuery(
    DateOnly FromDate,
    DateOnly ToDate,
    Guid? TripId = null,
    TicketScanAction? Action = null,
    TicketScanResult? Result = null,
    TicketScanSource? Source = null) : IRequest<IReadOnlyList<TicketScanEventDto>>;

public sealed class GetMyTicketScanHistoryQueryValidator : AbstractValidator<GetMyTicketScanHistoryQuery>
{
    public GetMyTicketScanHistoryQueryValidator()
    {
        RuleFor(x => x.ToDate)
            .GreaterThanOrEqualTo(x => x.FromDate)
            .WithMessage("toDate phải lớn hơn hoặc bằng fromDate.");
        RuleFor(x => x)
            .Must(x => x.FromDate.AddDays(62) >= x.ToDate)
            .WithMessage("Khoảng xem không được vượt quá 62 ngày.")
            .OverridePropertyName(nameof(GetMyTicketScanHistoryQuery.ToDate));
    }
}

public sealed class GetMyTicketScanHistoryQueryHandler
    : IRequestHandler<GetMyTicketScanHistoryQuery, IReadOnlyList<TicketScanEventDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;

    public GetMyTicketScanHistoryQueryHandler(
        IApplicationDbContext context,
        IUserContext userContext)
    {
        _context = context;
        _userContext = userContext;
    }

    public async Task<IReadOnlyList<TicketScanEventDto>> Handle(
        GetMyTicketScanHistoryQuery request,
        CancellationToken cancellationToken)
    {
        var actor = await AuthSupport.GetCurrentUserWithRoleAsync(_context, _userContext, cancellationToken);
        if (!AuthSupport.IsStaff(actor))
        {
            throw new ForbiddenAccessException();
        }

        var from = new DateTimeOffset(request.FromDate.ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(7))
            .ToUniversalTime();
        var to = new DateTimeOffset(request.ToDate.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(7))
            .ToUniversalTime();

        var query = TicketScanHistorySupport.BuildEventQuery(_context)
            .Where(x => x.PerformedByUserId == actor.Id
                && x.ServerTime >= from
                && x.ServerTime < to);

        if (request.TripId.HasValue)
            query = query.Where(x => x.TripId == request.TripId.Value);
        if (request.Action.HasValue)
            query = query.Where(x => x.Action == request.Action.Value);
        if (request.Result.HasValue)
            query = query.Where(x => x.Result == request.Result.Value);
        if (request.Source.HasValue)
            query = query.Where(x => x.Source == request.Source.Value);

        var events = await query
            .OrderByDescending(x => x.ServerTime)
            .ThenByDescending(x => x.Id)
            .ToListAsync(cancellationToken);

        return events.Select(TicketScanHistorySupport.ToDto).ToList();
    }
}

[Authorize]
public sealed record GetTicketScanHistoryQuery(Guid TicketId) : IRequest<IReadOnlyList<TicketScanEventDto>>;

public sealed class GetTicketScanHistoryQueryValidator : AbstractValidator<GetTicketScanHistoryQuery>
{
    public GetTicketScanHistoryQueryValidator()
    {
        RuleFor(x => x.TicketId).NotEmpty();
    }
}

public sealed class GetTicketScanHistoryQueryHandler
    : IRequestHandler<GetTicketScanHistoryQuery, IReadOnlyList<TicketScanEventDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;

    public GetTicketScanHistoryQueryHandler(
        IApplicationDbContext context,
        IUserContext userContext)
    {
        _context = context;
        _userContext = userContext;
    }

    public async Task<IReadOnlyList<TicketScanEventDto>> Handle(
        GetTicketScanHistoryQuery request,
        CancellationToken cancellationToken)
    {
        var actor = await AuthSupport.GetCurrentUserWithRoleAsync(_context, _userContext, cancellationToken);
        var ticket = await _context.Tickets
            .AsNoTracking()
            .Include(x => x.Booking)
            .SingleOrDefaultAsync(x => x.Id == request.TicketId, cancellationToken)
            ?? throw new NotFoundException("Ticket not found.");

        if (!AuthSupport.IsAdmin(actor)
            && !AuthSupport.IsManager(actor)
            && !AuthSupport.IsStaff(actor)
            && ticket.Booking.UserId != actor.Id)
        {
            throw new NotFoundException("Ticket not found.");
        }

        var events = await TicketScanHistorySupport.BuildEventQuery(_context)
            .Where(x => x.TicketId == request.TicketId)
            .OrderByDescending(x => x.ServerTime)
            .ThenByDescending(x => x.Id)
            .ToListAsync(cancellationToken);

        return events.Select(TicketScanHistorySupport.ToDto).ToList();
    }
}

internal static class TicketScanHistorySupport
{
    private static readonly TimeSpan VietnamOffset = TimeSpan.FromHours(7);

    public static IQueryable<TicketScanEvent> BuildEventQuery(IApplicationDbContext context) =>
        context.TicketScanEvents
            .AsNoTracking()
            .Include(x => x.Ticket)
            .Include(x => x.Booking)
            .Include(x => x.Trip)
            .Include(x => x.PerformedByUser)
            .Include(x => x.StaffWorkAssignment)
            .Include(x => x.Boat)
            .Include(x => x.Station)
            .Include(x => x.TripStop);

    public static TicketScanEventDto ToDto(TicketScanEvent scanEvent) =>
        new(
            scanEvent.Id,
            scanEvent.TicketId,
            scanEvent.Ticket?.TicketCode,
            scanEvent.BookingId,
            scanEvent.Booking?.BookingCode,
            scanEvent.TripId,
            scanEvent.Trip?.TripCode,
            scanEvent.PerformedByUserId,
            scanEvent.PerformedByUser.FullName,
            scanEvent.StaffWorkAssignmentId,
            scanEvent.StaffWorkAssignment?.AssignmentType.ToString(),
            scanEvent.BoatId,
            scanEvent.Boat?.Code,
            scanEvent.Boat?.Name,
            scanEvent.StationId,
            scanEvent.Station?.StationCode,
            scanEvent.Station?.StationName,
            scanEvent.TripStopId,
            scanEvent.TripStop?.StopOrder,
            scanEvent.Action.ToString(),
            scanEvent.Result.ToString(),
            scanEvent.Source.ToString(),
            scanEvent.FailureReason,
            scanEvent.ClientOperationId,
            scanEvent.DeviceTime,
            scanEvent.ServerTime,
            scanEvent.Note,
            scanEvent.ScannedCodeOrToken,
            scanEvent.TicketStatusBefore?.ToString(),
            scanEvent.TicketStatusAfter?.ToString());

    public static bool IsOperationalUser(User user) =>
        AuthSupport.IsAdmin(user) || AuthSupport.IsManager(user) || AuthSupport.IsStaff(user);

    public static bool IsLoggableFailure(Exception exception) =>
        exception is NotFoundException
            or ValidationException;

    public static string FailureReason(Exception exception)
    {
        if (exception is ValidationException validationException)
        {
            return string.Join(
                " ",
                validationException.Errors
                    .SelectMany(x => x.Value)
                    .Where(x => !string.IsNullOrWhiteSpace(x)));
        }

        return string.IsNullOrWhiteSpace(exception.Message)
            ? "Thao tác scan vé thất bại."
            : exception.Message;
    }

    public static async Task AddEventAsync(
        IApplicationDbContext context,
        User actor,
        TicketScanAction action,
        TicketScanResult result,
        TicketScanRequestMetadata metadata,
        DateTimeOffset serverTime,
        string codeOrToken,
        Ticket? ticket,
        TicketStatus? ticketStatusBefore,
        TicketStatus? ticketStatusAfter,
        string? failureReason,
        CancellationToken cancellationToken)
    {
        var staffContext = await ResolveStaffContextAsync(context, actor, ticket, metadata, serverTime, cancellationToken);
        var booking = ticket?.Booking;
        var trip = ticket?.BookingPassenger?.Trip ?? booking?.Trip;
        var normalizedCodeOrToken = NormalizeCodeOrToken(codeOrToken);

        var scanEvent = new TicketScanEvent
        {
            TicketId = ticket?.Id,
            BookingId = booking?.Id,
            TripId = trip?.Id ?? booking?.TripId,
            PerformedByUserId = actor.Id,
            StaffWorkAssignmentId = staffContext?.AssignmentId,
            BoatId = staffContext?.BoatId ?? trip?.BoatId ?? booking?.BoatId,
            StationId = staffContext?.StationId,
            TripStopId = staffContext?.TripStopId ?? metadata.TripStopId,
            Action = action,
            Result = result,
            FailureReason = NormalizeOptionalText(failureReason, 500),
            Source = metadata.Source,
            ClientOperationId = NormalizeOptionalText(metadata.ClientOperationId, 100),
            DeviceTime = metadata.DeviceTime,
            ServerTime = serverTime,
            Note = NormalizeOptionalText(metadata.Note, 500),
            ScannedCodeOrToken = normalizedCodeOrToken,
            TicketStatusBefore = ticketStatusBefore,
            TicketStatusAfter = ticketStatusAfter
        };

        context.TicketScanEvents.Add(scanEvent);
    }

    public static async Task SaveFailureEventAsync(
        IApplicationDbContext context,
        User actor,
        TicketScanAction action,
        TicketScanRequestMetadata metadata,
        DateTimeOffset serverTime,
        string codeOrToken,
        Ticket? ticket,
        TicketStatus? ticketStatusBefore,
        Exception exception,
        CancellationToken cancellationToken)
    {
        await AddEventAsync(
            context,
            actor,
            action,
            TicketScanResult.Failed,
            metadata,
            serverTime,
            codeOrToken,
            ticket,
            ticketStatusBefore,
            ticketStatusBefore,
            FailureReason(exception),
            cancellationToken);

        await context.SaveChangesAsync(cancellationToken);
    }

    private static async Task<ResolvedStaffScanContext?> ResolveStaffContextAsync(
        IApplicationDbContext context,
        User actor,
        Ticket? ticket,
        TicketScanRequestMetadata metadata,
        DateTimeOffset serverTime,
        CancellationToken cancellationToken)
    {
        if (!AuthSupport.IsStaff(actor))
        {
            return null;
        }

        var activeAssignments = await context.StaffWorkAssignments
            .AsNoTracking()
            .Include(x => x.Station)
            .Include(x => x.TripStop)
            .Where(x => x.StaffUserId == actor.Id
                && x.Status != StaffWorkAssignmentStatus.Cancelled
                && x.Status != StaffWorkAssignmentStatus.Replaced
                && x.StartAt <= serverTime
                && x.EndAt >= serverTime)
            .OrderBy(x => x.StartAt)
            .ToListAsync(cancellationToken);

        if (activeAssignments.Count == 0)
        {
            return null;
        }

        var assignment = ticket is null
            ? FindBestMatchingAssignment(activeAssignments, metadata.TripStopId)
            : FindBestMatchingAssignment(activeAssignments, ticket, metadata.TripStopId);

        return assignment is null
            ? null
            : new ResolvedStaffScanContext(assignment.Id, assignment.BoatId, assignment.StationId, assignment.TripStopId);
    }

    private static StaffWorkAssignment? FindBestMatchingAssignment(
        IReadOnlyList<StaffWorkAssignment> assignments,
        Guid? requestedTripStopId) =>
        requestedTripStopId.HasValue
            ? assignments.FirstOrDefault(x => x.TripStopId == requestedTripStopId.Value)
                ?? assignments.FirstOrDefault()
            : assignments.FirstOrDefault();

    private static StaffWorkAssignment? FindBestMatchingAssignment(
        IReadOnlyList<StaffWorkAssignment> assignments,
        Ticket ticket,
        Guid? requestedTripStopId)
    {
        var booking = ticket.Booking;
        var trip = ticket.BookingPassenger?.Trip ?? booking.Trip;
        var boardingTripStop = ResolveBoardingTripStop(ticket, trip);
        var routeStationIds = trip?.Route.RouteStops.Select(x => x.StationId).ToHashSet() ?? [];
        var charterStationIds = new[] { booking.FromStationId, booking.ToStationId }
            .Where(x => x.HasValue)
            .Select(x => x!.Value)
            .ToHashSet();

        return assignments
            .Where(assignment => AssignmentMatchesTicket(
                assignment,
                booking,
                trip,
                boardingTripStop,
                requestedTripStopId,
                routeStationIds,
                charterStationIds))
            .OrderBy(assignment => assignment.AssignmentType == StaffWorkAssignmentType.Boat ? 0 : 1)
            .ThenByDescending(assignment => assignment.TripStopId.HasValue)
            .ThenBy(assignment => assignment.StartAt)
            .FirstOrDefault()
            ?? assignments.FirstOrDefault();
    }

    private static bool AssignmentMatchesTicket(
        StaffWorkAssignment assignment,
        Booking booking,
        Trip? trip,
        TripStop? boardingTripStop,
        Guid? requestedTripStopId,
        IReadOnlySet<Guid> routeStationIds,
        IReadOnlySet<Guid> charterStationIds)
    {
        if (assignment.AssignmentType == StaffWorkAssignmentType.Boat)
        {
            var boatId = trip?.BoatId ?? booking.BoatId;
            return boatId.HasValue
                && assignment.BoatId.HasValue
                && assignment.BoatId.Value == boatId.Value;
        }

        if (assignment.AssignmentType == StaffWorkAssignmentType.Station && assignment.StationId.HasValue)
        {
            if (assignment.TripStopId.HasValue)
            {
                return requestedTripStopId.HasValue
                    ? assignment.TripStopId.Value == requestedTripStopId.Value
                    : boardingTripStop is not null && assignment.TripStopId.Value == boardingTripStop.Id;
            }

            return routeStationIds.Contains(assignment.StationId.Value)
                || charterStationIds.Contains(assignment.StationId.Value);
        }

        return false;
    }

    private static TripStop? ResolveBoardingTripStop(Ticket ticket, Trip? trip)
    {
        if (trip is null || trip.TripStops.Count == 0)
        {
            return null;
        }

        var fromStopOrder = ticket.BookingPassenger?.FromStopOrder
            ?? trip.TripStops.Min(x => x.StopOrder);
        return trip.TripStops.FirstOrDefault(x => x.StopOrder == fromStopOrder);
    }

    private static string? NormalizeCodeOrToken(string value)
    {
        var normalized = NormalizeOptionalText(value, 150);
        return normalized;
    }

    private static string? NormalizeOptionalText(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        return normalized.Length <= maxLength
            ? normalized
            : normalized[..maxLength];
    }

    public static DateOnly TodayInVietnam(DateTimeOffset now) =>
        DateOnly.FromDateTime(now.ToOffset(VietnamOffset).DateTime);

    private sealed record ResolvedStaffScanContext(
        Guid AssignmentId,
        Guid? BoatId,
        Guid? StationId,
        Guid? TripStopId);
}
