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
            incident.ReplacementMissionType,
            incident.ReplacementTargetStationId,
            incident.ReplacementTargetStation?.StationCode,
            incident.ReplacementTargetStation?.StationName,
            incident.ReplacementTargetStopOrder,
            incident.ReplacementDelayMinutes,
            incident.ReplacementEstimatedResumeAt,
            incident.OnboardPassengerCountSnapshot,
            incident.FuturePassengerCountSnapshot,
            incident.ReplacementNote,
            activeTicketCount > 0 ? activeTicketCount : incident.ActiveTicketCountSnapshot,
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
            incident.ReplacementMissionType,
            incident.ReplacementTargetStationId,
            incident.ReplacementTargetStation?.StationName,
            incident.ReplacementDelayMinutes,
            incident.ReplacementEstimatedResumeAt,
            incident.OnboardPassengerCountSnapshot,
            incident.FuturePassengerCountSnapshot,
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
                incident.ReplacementMissionType,
                incident.ReplacementTargetStationId,
                incident.ReplacementTargetStation?.StationCode,
                incident.ReplacementTargetStation?.StationName,
                incident.ReplacementTargetStopOrder,
                incident.ReplacementTargetStation?.Latitude,
                incident.ReplacementTargetStation?.Longitude,
                incident.ReplacementDelayMinutes,
                incident.ReplacementEstimatedResumeAt,
                incident.OnboardPassengerCountSnapshot,
                incident.FuturePassengerCountSnapshot,
                location?.Latitude,
                location?.Longitude),
            cancellationToken);
    }

    public static async Task<IncidentPassengerImpactPlan> BuildPassengerImpactPlanAsync(
        IApplicationDbContext context,
        Incident incident,
        CancellationToken cancellationToken)
    {
        if (!incident.TripId.HasValue)
        {
            return IncidentPassengerImpactPlan.Empty;
        }

        var trip = incident.Trip ?? await context.Set<Trip>()
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == incident.TripId.Value, cancellationToken);
        if (trip is null)
        {
            return IncidentPassengerImpactPlan.Empty;
        }

        var stops = await LoadTripStopPlanAsync(context, trip, cancellationToken);
        var activeTicketSegments = await LoadActiveTicketSegmentsAsync(
            context,
            trip.Id,
            cancellationToken);
        if (activeTicketSegments.Count == 0)
        {
            return new IncidentPassengerImpactPlan(
                ActiveTicketCount: 0,
                OnboardPassengerCount: 0,
                FuturePassengerCount: 0,
                IncidentReplacementMissionTypes.None,
                TargetStationId: null,
                TargetStationCode: null,
                TargetStationName: null,
                TargetStopOrder: null,
                TargetPlannedArrivalAt: null,
                TargetPlannedDepartureAt: null);
        }

        if (stops.Count == 0)
        {
            return BuildUnknownProgressPlan(activeTicketSegments.Count);
        }

        var firstStopOrder = stops.Min(x => x.StopOrder);
        var lastStopOrder = stops.Max(x => x.StopOrder);
        var currentProgressStopOrder = await ResolveCurrentProgressStopOrderAsync(
            context,
            incident,
            trip,
            stops,
            firstStopOrder,
            cancellationToken);
        if (!currentProgressStopOrder.HasValue)
        {
            return BuildUnknownProgressPlan(activeTicketSegments.Count);
        }

        var progressStopOrder = currentProgressStopOrder.Value;
        var onboardPassengerCount = 0;
        var futurePassengerCount = 0;
        int? nextPassengerBoardingStopOrder = null;

        foreach (var segment in activeTicketSegments)
        {
            var fromStopOrder = NormalizeFromStopOrder(segment.FromStopOrder, firstStopOrder, lastStopOrder);
            var toStopOrder = NormalizeToStopOrder(segment.ToStopOrder, fromStopOrder, lastStopOrder);

            if (toStopOrder <= progressStopOrder)
            {
                continue;
            }

            if (fromStopOrder <= progressStopOrder)
            {
                onboardPassengerCount++;
                continue;
            }

            futurePassengerCount++;
            nextPassengerBoardingStopOrder = !nextPassengerBoardingStopOrder.HasValue
                ? fromStopOrder
                : Math.Min(nextPassengerBoardingStopOrder.Value, fromStopOrder);
        }

        var affectedPassengerCount = onboardPassengerCount + futurePassengerCount;
        if (affectedPassengerCount == 0)
        {
            return new IncidentPassengerImpactPlan(
                activeTicketSegments.Count,
                OnboardPassengerCount: 0,
                FuturePassengerCount: 0,
                IncidentReplacementMissionTypes.None,
                TargetStationId: null,
                TargetStationCode: null,
                TargetStationName: null,
                TargetStopOrder: null,
                TargetPlannedArrivalAt: null,
                TargetPlannedDepartureAt: null);
        }

        if (onboardPassengerCount > 0)
        {
            return new IncidentPassengerImpactPlan(
                activeTicketSegments.Count,
                onboardPassengerCount,
                futurePassengerCount,
                IncidentReplacementMissionTypes.TransferAtIncidentLocation,
                TargetStationId: null,
                TargetStationCode: null,
                TargetStationName: null,
                TargetStopOrder: null,
                TargetPlannedArrivalAt: null,
                TargetPlannedDepartureAt: null);
        }

        var targetStop = nextPassengerBoardingStopOrder.HasValue
            ? stops.FirstOrDefault(x => x.StopOrder == nextPassengerBoardingStopOrder.Value)
            : null;

        return new IncidentPassengerImpactPlan(
            activeTicketSegments.Count,
            onboardPassengerCount,
            futurePassengerCount,
            targetStop is null
                ? IncidentReplacementMissionTypes.PassengerRecoveryRequired
                : IncidentReplacementMissionTypes.ContinueFromStation,
            targetStop?.StationId,
            targetStop?.StationCode,
            targetStop?.StationName,
            targetStop?.StopOrder,
            targetStop?.PlannedArrivalTime,
            targetStop?.PlannedDepartureTime);
    }

    private static IncidentPassengerImpactPlan BuildUnknownProgressPlan(int activeTicketCount) =>
        new(
            activeTicketCount,
            OnboardPassengerCount: activeTicketCount,
            FuturePassengerCount: 0,
            IncidentReplacementMissionTypes.PassengerRecoveryRequired,
            TargetStationId: null,
            TargetStationCode: null,
            TargetStationName: null,
            TargetStopOrder: null,
            TargetPlannedArrivalAt: null,
            TargetPlannedDepartureAt: null);

    private static async Task<IReadOnlyList<IncidentStopPlanItem>> LoadTripStopPlanAsync(
        IApplicationDbContext context,
        Trip trip,
        CancellationToken cancellationToken)
    {
        var tripStops = await context.Set<TripStop>()
            .AsNoTracking()
            .Include(x => x.Station)
            .Where(x => x.TripId == trip.Id)
            .OrderBy(x => x.StopOrder)
            .Select(x => new IncidentStopPlanItem(
                x.StationId,
                x.Station.StationCode,
                x.Station.StationName,
                x.StopOrder,
                x.PlannedArrivalTime,
                x.PlannedDepartureTime,
                x.StopStatus,
                x.ActualArrivalTime,
                x.ActualDepartureTime))
            .ToListAsync(cancellationToken);

        if (tripStops.Count > 0)
        {
            return tripStops;
        }

        var routeStops = await context.Set<RouteStop>()
            .AsNoTracking()
            .Include(x => x.Station)
            .Where(x => x.RouteId == trip.RouteId)
            .OrderBy(x => x.StopOrder)
            .ToListAsync(cancellationToken);

        return SaigonWaterbus.Application.Trips.TripStopScheduleSupport
            .BuildFromRouteStops(routeStops, trip.DepartureTime)
            .Select(x => new IncidentStopPlanItem(
                x.StationId,
                x.Station?.StationCode ?? string.Empty,
                x.Station?.StationName ?? string.Empty,
                x.StopOrder,
                x.PlannedArrivalTime,
                x.PlannedDepartureTime,
                null,
                null,
                null))
            .ToList();
    }

    private static async Task<int?> ResolveCurrentProgressStopOrderAsync(
        IApplicationDbContext context,
        Incident incident,
        Trip trip,
        IReadOnlyList<IncidentStopPlanItem> stops,
        int firstStopOrder,
        CancellationToken cancellationToken)
    {
        var stationStopOrders = stops.ToDictionary(x => x.StationId, x => x.StopOrder);
        var latestLocation = await context.BoatLatestLocations
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.BoatId == incident.BoatId, cancellationToken);
        if (latestLocation?.TripId == trip.Id
            && latestLocation.NextStationId.HasValue
            && stationStopOrders.TryGetValue(latestLocation.NextStationId.Value, out var nextStopOrder))
        {
            return Math.Max(firstStopOrder - 1, nextStopOrder - 1);
        }

        var currentAtStation = stops
            .Where(x => string.Equals(x.StopStatus, TripStopStatuses.Arrived, StringComparison.OrdinalIgnoreCase)
                && x.ActualDepartureTime is null)
            .OrderByDescending(x => x.ActualArrivalTime ?? DateTimeOffset.MinValue)
            .ThenByDescending(x => x.StopOrder)
            .FirstOrDefault();
        if (currentAtStation is not null)
        {
            return currentAtStation.StopOrder;
        }

        var lastDeparted = stops
            .Where(x => x.ActualDepartureTime.HasValue)
            .OrderByDescending(x => x.ActualDepartureTime)
            .ThenByDescending(x => x.StopOrder)
            .FirstOrDefault();
        if (lastDeparted is not null)
        {
            return lastDeparted.StopOrder;
        }

        var arrivingStop = stops
            .Where(x => string.Equals(x.StopStatus, TripStopStatuses.Arriving, StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.StopOrder)
            .FirstOrDefault();
        if (arrivingStop is not null)
        {
            return Math.Max(firstStopOrder - 1, arrivingStop.StopOrder - 1);
        }

        if (trip.TripStatus is TripStatus.Scheduled or TripStatus.Boarding or TripStatus.Delayed)
        {
            return firstStopOrder - 1;
        }

        return null;
    }

    private static async Task<IReadOnlyList<TicketTripSegment>> LoadActiveTicketSegmentsAsync(
        IApplicationDbContext context,
        Guid tripId,
        CancellationToken cancellationToken) =>
        await context.Tickets
            .AsNoTracking()
            .Include(x => x.Booking)
            .Include(x => x.BookingPassenger)
            .Where(x => (x.BookingPassenger != null && x.BookingPassenger.TripId != null
                        ? x.BookingPassenger.TripId == tripId
                        : x.Booking.TripId == tripId)
                    && x.TicketStatus != TicketStatus.Cancelled
                    && x.TicketStatus != TicketStatus.Expired)
            .Select(x => new TicketTripSegment(
                x.BookingPassenger != null ? x.BookingPassenger.FromStopOrder : null,
                x.BookingPassenger != null ? x.BookingPassenger.ToStopOrder : null))
            .ToListAsync(cancellationToken);

    private static int NormalizeFromStopOrder(int? fromStopOrder, int firstStopOrder, int lastStopOrder)
    {
        if (!fromStopOrder.HasValue)
        {
            return firstStopOrder;
        }

        return Math.Clamp(fromStopOrder.Value, firstStopOrder, lastStopOrder);
    }

    private static int NormalizeToStopOrder(int? toStopOrder, int fromStopOrder, int lastStopOrder)
    {
        if (!toStopOrder.HasValue || toStopOrder.Value <= fromStopOrder)
        {
            return lastStopOrder;
        }

        return Math.Clamp(toStopOrder.Value, fromStopOrder + 1, lastStopOrder);
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

    public sealed record IncidentPassengerImpactPlan(
        int ActiveTicketCount,
        int OnboardPassengerCount,
        int FuturePassengerCount,
        string ReplacementMissionType,
        Guid? TargetStationId,
        string? TargetStationCode,
        string? TargetStationName,
        int? TargetStopOrder,
        DateTimeOffset? TargetPlannedArrivalAt,
        DateTimeOffset? TargetPlannedDepartureAt)
    {
        public static IncidentPassengerImpactPlan Empty { get; } = new(
            ActiveTicketCount: 0,
            OnboardPassengerCount: 0,
            FuturePassengerCount: 0,
            IncidentReplacementMissionTypes.None,
            TargetStationId: null,
            TargetStationCode: null,
            TargetStationName: null,
            TargetStopOrder: null,
            TargetPlannedArrivalAt: null,
            TargetPlannedDepartureAt: null);

        public int AffectedPassengerCount => OnboardPassengerCount + FuturePassengerCount;
    }

    private sealed record IncidentStopPlanItem(
        Guid StationId,
        string StationCode,
        string StationName,
        int StopOrder,
        DateTimeOffset? PlannedArrivalTime,
        DateTimeOffset? PlannedDepartureTime,
        string? StopStatus,
        DateTimeOffset? ActualArrivalTime,
        DateTimeOffset? ActualDepartureTime);

    private sealed record TicketTripSegment(int? FromStopOrder, int? ToStopOrder);
}
