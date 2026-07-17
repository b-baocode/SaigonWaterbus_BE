using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Exceptions;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Application.Incidents;

internal static class IncidentSupport
{
    public const string OpenStatus = "Open";
    public const string ResolvedStatus = "Resolved";
    public const string IncidentCreatedEvent = "IncidentCreated";
    public const string RescueDispatchedEvent = "RescueDispatched";
    public const string IncidentResolvedEvent = "IncidentResolved";
    public const string CriticalSeverity = "Critical";
    public const string HighSeverity = "High";

    public static async Task<User> EnsureCurrentUserCanReportIncidentAsync(
        IApplicationDbContext context,
        IUserContext userContext,
        CancellationToken cancellationToken)
    {
        var actor = await AuthSupport.GetCurrentUserWithRoleAsync(context, userContext, cancellationToken);
        if (AuthSupport.IsAdmin(actor) || AuthSupport.IsManager(actor) || AuthSupport.IsStaff(actor))
        {
            return actor;
        }

        throw new ForbiddenAccessException();
    }

    public static async Task<User> EnsureCurrentUserCanResolveIncidentAsync(
        IApplicationDbContext context,
        IUserContext userContext,
        CancellationToken cancellationToken)
    {
        var actor = await AuthSupport.GetCurrentUserWithRoleAsync(context, userContext, cancellationToken);
        if (AuthSupport.IsAdmin(actor) || AuthSupport.IsManager(actor))
        {
            return actor;
        }

        throw new ForbiddenAccessException();
    }

    public static async Task<User> EnsureCurrentUserCanAssignIncidentManagerAsync(
        IApplicationDbContext context,
        IUserContext userContext,
        CancellationToken cancellationToken)
    {
        var actor = await AuthSupport.GetCurrentUserWithRoleAsync(context, userContext, cancellationToken);
        if (AuthSupport.IsAdmin(actor))
        {
            return actor;
        }

        throw new ForbiddenAccessException();
    }

    public static async Task EnsureUserIsActiveManagerAsync(
        IApplicationDbContext context,
        Guid managerUserId,
        string propertyName,
        CancellationToken cancellationToken)
    {
        var manager = await context.Users
            .Include(x => x.Role)
            .SingleOrDefaultAsync(x => x.Id == managerUserId, cancellationToken)
            ?? throw new SaigonWaterbus.Application.Common.Exceptions.NotFoundException("Không tìm thấy manager.");

        if (!string.Equals(manager.Role.SystemName, Roles.ManagerSystemName, StringComparison.Ordinal))
        {
            throw AuthSupport.CreateValidationException(propertyName, "Người được gán phải có role Manager.");
        }

        if (manager.Status != UserStatus.Active)
        {
            throw AuthSupport.CreateValidationException(propertyName, "Manager phải đang Active.");
        }
    }

    public static IncidentDto ToDto(Incident incident, int activeTicketCount = 0) =>
        new(
            incident.Id,
            incident.BoatId,
            incident.Boat.Name,
            incident.TripId,
            incident.Trip?.TripCode,
            incident.IncidentType,
            incident.Description,
            incident.Severity,
            incident.OccurredAt,
            incident.ResolutionStatus,
            incident.ReportedBy,
            incident.Reporter?.FullName,
            incident.AssignedManagerId,
            incident.AssignedManager?.FullName,
            incident.AssignedAt,
            incident.AssignedByUserId,
            incident.AssignedByUser?.FullName,
            incident.RescueBoatId,
            incident.RescueBoat?.Name,
            incident.RescueDispatchedAt,
            incident.RescueDispatchedByUserId,
            incident.RescueDispatchedByUser?.FullName,
            incident.ReplacementBoatId,
            incident.ReplacementBoat?.Name,
            incident.ReplacementAssignedAt,
            incident.ReplacementAssignedByUserId,
            incident.ReplacementAssignedByUser?.FullName,
            incident.ReplacementNote,
            activeTicketCount,
            incident.ResolutionNote,
            incident.ResolvedAt,
            incident.ResolvedByUserId,
            incident.Resolver?.FullName);

    public static IncidentRealtimeEvent ToRealtimeEvent(
        Incident incident,
        string eventType,
        DateTimeOffset? occurredAt = null) =>
        new(
            incident.Id,
            eventType,
            incident.BoatId,
            incident.Boat?.Name,
            incident.TripId,
            incident.Trip?.TripCode,
            incident.RescueBoatId,
            incident.RescueBoat?.Name,
            incident.ReplacementBoatId,
            incident.ReplacementBoat?.Name,
            incident.ResolutionStatus,
            occurredAt);

    public static async Task PublishGpsHookAsync(
        IApplicationDbContext context,
        IIncidentGpsHookNotifier gpsHookNotifier,
        Incident incident,
        string eventType,
        CancellationToken cancellationToken)
    {
        var location = await context.BoatLatestLocations
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.BoatId == incident.BoatId, cancellationToken);

        await gpsHookNotifier.NotifyAsync(
            new IncidentGpsHookNotification(
                eventType,
                incident.Id,
                incident.Boat.Code,
                incident.RescueBoat?.Code,
                incident.ReplacementBoat?.Code,
                location?.Latitude,
                location?.Longitude),
            cancellationToken);
    }

    public static Task<int> CountActiveTicketsAsync(
        IApplicationDbContext context,
        Guid tripId,
        CancellationToken cancellationToken) =>
        context.Tickets.CountAsync(
            x => (x.BookingPassenger != null && x.BookingPassenger.TripId != null
                    ? x.BookingPassenger.TripId == tripId
                    : x.Booking.TripId == tripId)
              && x.TicketStatus != TicketStatus.Cancelled
              && x.TicketStatus != TicketStatus.Expired,
            cancellationToken);

    public static async Task<IReadOnlyDictionary<Guid, int>> CountActiveTicketsByTripAsync(
        IApplicationDbContext context,
        IReadOnlyCollection<Guid> tripIds,
        CancellationToken cancellationToken)
    {
        if (tripIds.Count == 0)
        {
            return new Dictionary<Guid, int>();
        }

        var passengerTicketCounts = await context.Tickets
            .AsNoTracking()
            .Where(x => x.BookingPassenger != null
                && x.BookingPassenger.TripId != null
                && tripIds.Contains(x.BookingPassenger.TripId.Value)
                && x.TicketStatus != TicketStatus.Cancelled
                && x.TicketStatus != TicketStatus.Expired)
            .GroupBy(x => x.BookingPassenger!.TripId!.Value)
            .Select(x => new { TripId = x.Key, Count = x.Count() })
            .ToListAsync(cancellationToken);

        var legacyTicketCounts = await context.Tickets
            .AsNoTracking()
            .Where(x => (x.BookingPassenger == null || x.BookingPassenger.TripId == null)
                && x.Booking.TripId != null
                && tripIds.Contains(x.Booking.TripId.Value)
                && x.TicketStatus != TicketStatus.Cancelled
                && x.TicketStatus != TicketStatus.Expired)
            .GroupBy(x => x.Booking.TripId!.Value)
            .Select(x => new { TripId = x.Key, Count = x.Count() })
            .ToListAsync(cancellationToken);

        return passengerTicketCounts
            .Concat(legacyTicketCounts)
            .GroupBy(x => x.TripId)
            .ToDictionary(x => x.Key, x => x.Sum(item => item.Count));
    }
}
