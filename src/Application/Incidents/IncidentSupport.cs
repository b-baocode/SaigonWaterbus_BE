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

    public static IncidentDto ToDto(Incident incident) =>
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
            incident.ReplacementBoatId,
            incident.ReplacementBoat?.Name,
            incident.ReplacementAssignedAt,
            incident.ReplacementAssignedByUserId,
            incident.ReplacementAssignedByUser?.FullName,
            incident.ReplacementNote,
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
            incident.ReplacementBoatId,
            incident.ReplacementBoat?.Name,
            incident.ResolutionStatus,
            occurredAt);
}
