using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common;
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
    public const string IncidentLocationStatus = "incident";
    public const string MaintenanceLocationStatus = "maintenance";

    public static async Task<User> EnsureCurrentUserCanReportIncidentAsync(
        IApplicationDbContext context,
        IUserContext userContext,
        CancellationToken cancellationToken)
    {
        var actor = await AuthSupport.GetCurrentUserWithRoleAsync(context, userContext, cancellationToken);
        if (AuthSupport.IsAdmin(actor) || AuthSupport.IsStaff(actor))
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

    public static void EnsureManagerCanAccessIncident(User actor, Incident incident)
    {
        if (!AuthSupport.IsManager(actor))
        {
            return;
        }

        if (incident.AssignedManagerId == actor.Id)
        {
            return;
        }

        throw new ForbiddenAccessException();
    }

    public static IncidentDto ToDto(Incident incident, int activeTicketCount = 0) =>
        new(
            incident.Id,
            incident.BoatId,
            incident.Boat.Name,
            incident.Boat.Code,
            incident.TripId,
            incident.Trip?.TripCode,
            incident.IncidentType,
            incident.Description,
            incident.Severity,
            incident.OccurredAt,
            incident.ResolutionStatus,
            incident.ReportedBy,
            incident.Reporter?.FullName,
            incident.AssignedAt,
            incident.AssignedByUserId,
            incident.AssignedByUser?.FullName,
            incident.RescueBoatId,
            incident.RescueBoat?.Name,
            incident.RescueBoat?.Code,
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
            incident.ReplacementTargetStation?.StationName,
            incident.ReplacementTargetStopOrder,
            incident.ReplacementDelayMinutes,
            incident.ReplacementEstimatedResumeAt,
            activeTicketCount > 0 ? activeTicketCount : incident.ActiveTicketCountSnapshot,
            incident.OnboardPassengerCountSnapshot,
            incident.FuturePassengerCountSnapshot,
            incident.ResolutionNote,
            incident.ResolvedAt);

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
            OperatingStatusSupport.ToPublicMissionStatus(incident),
            OperatingStatusSupport.ForIncident(incident),
            incident.RescueArrivedAt,
            incident.ReplacementArrivedAt,
            incident.PassengerTransferCompletedAt,
            incident.TowingStartedAt,
            incident.TowingCompletedAt,
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
                incident.TripId,
                incident.Trip?.TripCode,
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

    public static async Task ClearBoatLiveTripAsync(
        IApplicationDbContext context,
        Guid boatId,
        DateTimeOffset clearedAt,
        string status,
        CancellationToken cancellationToken)
    {
        var latestLocation = await context.BoatLatestLocations
            .SingleOrDefaultAsync(x => x.BoatId == boatId, cancellationToken);
        if (latestLocation is null)
        {
            return;
        }

        latestLocation.RouteId = null;
        latestLocation.TripId = null;
        latestLocation.NextStationId = null;
        latestLocation.RemainingDistanceKmToNextStation = null;
        latestLocation.RemainingMinutesToNextStation = null;
        latestLocation.SpeedKmh = 0;
        latestLocation.Status = status;
        latestLocation.ReceivedAt = clearedAt;
        latestLocation.UpdatedAt = clearedAt;
    }

    public static void EnsureTripIsNotRunningOnMaintainedBoat(Incident incident)
    {
        if (incident.Trip is null
            || incident.Trip.BoatId != incident.BoatId
            || incident.Trip.TripStatus is TripStatus.Completed or TripStatus.Cancelled)
        {
            return;
        }

        if (incident.ReplacementBoatId.HasValue)
        {
            incident.Trip.TripStatus = TripStatus.Delayed;
            incident.Trip.StatusNote = incident.ReplacementBoat is null
                ? "Tàu lỗi đã vào bảo trì, chuyến đang chờ tàu thay thế."
                : $"Tàu lỗi đã vào bảo trì, chuyến đang chờ tàu thay thế {incident.ReplacementBoat.Name}.";
            return;
        }

        incident.Trip.TripStatus = TripStatus.Cancelled;
        incident.Trip.StatusNote = "Chuyến đã hủy do tàu gặp sự cố và được đưa vào bảo trì.";
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

        if (stops.Count == 0)
        {
            return BuildUnknownProgressPlan(activeTicketSegments);
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
            return BuildUnknownProgressPlan(activeTicketSegments);
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

            if (segment.IsOnboard)
            {
                onboardPassengerCount++;
                continue;
            }

            if (segment.CanBoardLater && fromStopOrder > progressStopOrder)
            {
                futurePassengerCount++;
                nextPassengerBoardingStopOrder = !nextPassengerBoardingStopOrder.HasValue
                    ? fromStopOrder
                    : Math.Min(nextPassengerBoardingStopOrder.Value, fromStopOrder);
            }
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

    private static IncidentPassengerImpactPlan BuildUnknownProgressPlan(
        IReadOnlyList<TicketTripSegment> activeTicketSegments)
    {
        var onboardPassengerCount = activeTicketSegments.Count(x => x.IsOnboard);
        var futurePassengerCount = activeTicketSegments.Count(x => x.CanBoardLater);
        var replacementMissionType = onboardPassengerCount > 0
            ? IncidentReplacementMissionTypes.TransferAtIncidentLocation
            : futurePassengerCount > 0
                ? IncidentReplacementMissionTypes.PassengerRecoveryRequired
                : IncidentReplacementMissionTypes.None;

        return new(
            activeTicketSegments.Count,
            onboardPassengerCount,
            futurePassengerCount,
            replacementMissionType,
            TargetStationId: null,
            TargetStationCode: null,
            TargetStationName: null,
            TargetStopOrder: null,
            TargetPlannedArrivalAt: null,
            TargetPlannedDepartureAt: null);
    }

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
        var routeInfo = await context.Set<Route>()
            .AsNoTracking()
            .Where(x => x.Id == trip.RouteId)
            .Select(x => new { x.RouteType, x.EstimatedDurationMin })
            .SingleAsync(cancellationToken);

        return SaigonWaterbus.Application.Trips.TripStopScheduleSupport
            .BuildFromRouteStops(
                routeStops,
                trip.DepartureTime,
                routeType: routeInfo.RouteType,
                routeEstimatedDurationMin: routeInfo.EstimatedDurationMin)
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
        var latestLocation = await context.BoatLatestLocations
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.BoatId == incident.BoatId, cancellationToken);
        if (latestLocation?.TripId == trip.Id
            && latestLocation.NextStationId.HasValue)
        {
            var nextStopOrder = ResolveNextStopOrder(
                stops,
                latestLocation.NextStationId.Value,
                trip.TripStatus);
            if (nextStopOrder.HasValue)
            {
                return Math.Max(firstStopOrder - 1, nextStopOrder.Value - 1);
            }
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

    private static int? ResolveNextStopOrder(
        IReadOnlyList<IncidentStopPlanItem> stops,
        Guid nextStationId,
        TripStatus tripStatus)
    {
        var matchingStops = stops
            .Where(x => x.StationId == nextStationId)
            .OrderBy(x => x.StopOrder)
            .ToList();
        if (matchingStops.Count == 0)
        {
            return null;
        }

        var lastDepartedStopOrder = stops
            .Where(x => x.ActualDepartureTime.HasValue)
            .Select(x => (int?)x.StopOrder)
            .Max();
        if (lastDepartedStopOrder.HasValue)
        {
            return matchingStops
                .FirstOrDefault(x => x.StopOrder > lastDepartedStopOrder.Value)
                ?.StopOrder
                ?? matchingStops[^1].StopOrder;
        }

        return tripStatus is TripStatus.Scheduled or TripStatus.Boarding or TripStatus.Delayed
            ? matchingStops[0].StopOrder
            : matchingStops[^1].StopOrder;
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
                x.BookingPassenger != null ? x.BookingPassenger.ToStopOrder : null,
                x.TicketStatus,
                x.CheckedOutAt))
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
        context.Tickets
            .AsNoTracking()
            .CountAsync(x => (x.BookingPassenger != null && x.BookingPassenger.TripId != null
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

        var result = await context.Tickets
            .AsNoTracking()
            .Where(x => tripIds.Contains(x.BookingPassenger != null && x.BookingPassenger.TripId != null
                    ? x.BookingPassenger.TripId.Value
                    : x.Booking.TripId!.Value)
                && x.TicketStatus != TicketStatus.Cancelled
                && x.TicketStatus != TicketStatus.Expired)
            .GroupBy(x => x.BookingPassenger != null && x.BookingPassenger.TripId != null
                ? x.BookingPassenger.TripId!.Value
                : x.Booking.TripId!.Value)
            .Select(g => new { TripId = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        return result.ToDictionary(x => x.TripId, x => x.Count);
    }
}
