using Microsoft.Extensions.Logging;
using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Bookings;
using SaigonWaterbus.Application.Common.Exceptions;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Application.Fares;
using SaigonWaterbus.Application.Trips;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Application.Operations;

public sealed record OperationScheduleItemDto(
    Guid Id,
    string SourceType,
    Guid SourceId,
    string SourceCode,
    string Title,
    Guid? BoatId,
    string? BoatCode,
    string? BoatName,
    Guid? RouteId,
    string? RouteCode,
    string? RouteName,
    string RouteType,
    string TripType,
    string ServiceType,
    bool SellsBySegment,
    int CapacitySnapshot,
    int TotalPassengerCount,
    Guid? FromStationId,
    string? FromStationCode,
    string FromLocation,
    Guid? ToStationId,
    string? ToStationCode,
    string ToLocation,
    DateOnly OperatingDate,
    DateTimeOffset StartAt,
    DateTimeOffset ScheduledDepartureAt,
    int? MinutesUntilDeparture,
    DateTimeOffset EndAt,
    string Status,
    string ScheduleState,
    string OperationStatus,
    string? PaymentStage,
    DateTimeOffset? RemainingPaymentDeadline,
    bool IsPaymentOverdue,
    int DelayMinutes,
    string? DelayReason,
    DateTimeOffset? AdjustedStartAt,
    DateTimeOffset? AdjustedEndAt,
    DateTimeOffset? ActualStartAt,
    DateTimeOffset? ActualEndAt,
    DateTimeOffset SyncedAt,
    string MovementStatus,
    Guid? CurrentStationId,
    string? CurrentStationCode,
    string? CurrentStationName,
    Guid? NextStationId,
    string? NextStationCode,
    string? NextStationName,
    string? LastStopEvent,
    DateTimeOffset? LastStopEventAt,
    DateTimeOffset? NextPlannedArrivalAt,
    decimal? LatestLatitude,
    decimal? LatestLongitude,
    decimal? LatestSpeedKmh,
    int? LatestHeading,
    DateTimeOffset? LatestGpsAt,
    decimal? RemainingDistanceKmToNextStation,
    int? RemainingMinutesToNextStation,
    bool IsGpsOnline,
    TripStopDwellCountdownDto? DwellCountdown = null,
    IReadOnlyList<OperationScheduleStopDto>? Stops = null,
    Guid? DestinationStationId = null,
    string? DestinationStationCode = null,
    string? DestinationStationName = null);

public sealed record OperationScheduleStopDto(
    Guid? TripStopId,
    int StopOrder,
    Guid StationId,
    string? StationCode,
    string StationName,
    DateTimeOffset? ScheduledArrivalAt,
    DateTimeOffset? ScheduledDepartureAt,
    DateTimeOffset? AdjustedArrivalAt,
    DateTimeOffset? AdjustedDepartureAt,
    DateTimeOffset? ActualArrivalAt,
    DateTimeOffset? ActualDepartureAt,
    int StayDurationMinutes,
    string? StopStatus);

public sealed record GetOperationScheduleQuery(
    DateTimeOffset From,
    DateTimeOffset To,
    bool IncludeCancelled = false,
    string? ServiceType = null,
    string? RouteType = null,
    Guid? StationId = null) : IRequest<IReadOnlyList<OperationScheduleItemDto>>;

public sealed class GetOperationScheduleQueryHandler
    : IRequestHandler<GetOperationScheduleQuery, IReadOnlyList<OperationScheduleItemDto>>
{
    private const int GpsOnlineThresholdSeconds = 60;

    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;
    private readonly TimeProvider _timeProvider;

    public GetOperationScheduleQueryHandler(
        IApplicationDbContext context,
        IUserContext userContext,
        TimeProvider timeProvider)
    {
        _context = context;
        _userContext = userContext;
        _timeProvider = timeProvider;
    }

    public async Task<IReadOnlyList<OperationScheduleItemDto>> Handle(
        GetOperationScheduleQuery request,
        CancellationToken cancellationToken)
    {
        var from = request.From.ToUniversalTime();
        var to = request.To.ToUniversalTime();
        if (to <= from)
        {
            throw AuthSupport.CreateValidationException(nameof(request.To), "Khoảng thời gian lịch không hợp lệ.");
        }

        var access = await ResolveAccessAsync(request, from, to, cancellationToken);
        if (access.MaxDays.HasValue && (int)Math.Ceiling((to - from).TotalDays) > access.MaxDays.Value)
        {
            throw AuthSupport.CreateValidationException(
                nameof(request.To),
                $"Customer chỉ được xem lịch tối đa {access.MaxDays.Value} ngày.");
        }

        var query = _context.Set<Trip>()
            .AsNoTracking()
            .Include(x => x.Boat)
            .Include(x => x.Route)
                .ThenInclude(x => x.RouteStops)
                    .ThenInclude(x => x.Station)
            .Include(x => x.TripStops)
                .ThenInclude(x => x.Station)
            .Where(x => x.DepartureTime < to && x.ArrivalTime >= from);

        var routeType = ResolveRouteType(request.RouteType);
        if (routeType is not null)
        {
            query = query.Where(x => x.Route.RouteType == routeType);
        }

        var serviceType = access.ForcedServiceType ?? ResolveServiceType(request.ServiceType);
        query = ApplyServiceTypeFilter(query, serviceType);

        if (!request.IncludeCancelled || !access.CanIncludeCancelled)
        {
            query = query.Where(x => x.TripStatus != TripStatus.Cancelled);
        }

        if (access.AllowedStationIds is not null)
        {
            if (access.AllowedStationIds.Count == 0)
            {
                return [];
            }

            var stationIds = access.AllowedStationIds.ToArray();
            query = query.Where(x => x.TripStops.Any(stop => stationIds.Contains(stop.StationId))
                || x.Route.RouteStops.Any(stop => stationIds.Contains(stop.StationId)));
        }

        var trips = await query
            .OrderBy(x => x.DepartureTime)
            .ThenBy(x => x.TripCode)
            .ToListAsync(cancellationToken);

        var boatIds = trips
            .Where(x => x.BoatId.HasValue)
            .Select(x => x.BoatId!.Value)
            .Distinct()
            .ToArray();
        var latestLocations = boatIds.Length == 0
            ? new Dictionary<Guid, BoatLatestLocation>()
            : await _context.BoatLatestLocations
                .AsNoTracking()
                .Where(x => boatIds.Contains(x.BoatId))
                .ToDictionaryAsync(x => x.BoatId, cancellationToken);
        if (!access.CanViewLiveTelemetry)
        {
            latestLocations.Clear();
        }

        var now = _timeProvider.GetUtcNow();
        var tripIds = trips.Select(x => x.Id).ToArray();
        var passengerCounts = tripIds.Length == 0
            ? new Dictionary<Guid, int>()
            : await _context.Set<BookingPassenger>()
                .AsNoTracking()
                .Where(x => (x.TripId.HasValue && tripIds.Contains(x.TripId.Value))
                    || (!x.TripId.HasValue && x.Booking.TripId.HasValue && tripIds.Contains(x.Booking.TripId.Value)))
                .Where(BookingSeatOccupancySupport.PassengerOccupiesSeat(now))
                .Select(x => new { TripId = x.TripId ?? x.Booking.TripId })
                .Where(x => x.TripId.HasValue)
                .GroupBy(x => x.TripId!.Value)
                .Select(g => new { TripId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.TripId, x => x.Count, cancellationToken);

        return trips
            .Select(trip =>
            {
                latestLocations.TryGetValue(trip.BoatId ?? Guid.Empty, out var latestLocation);
                var tripLatestLocation = latestLocation?.TripId == trip.Id ? latestLocation : null;
                return ToOperationScheduleItem(
                    trip,
                    tripLatestLocation,
                    now,
                    passengerCounts.GetValueOrDefault(trip.Id));
            })
            .ToArray();
    }

    private static OperationScheduleItemDto ToOperationScheduleItem(
        Trip trip,
        BoatLatestLocation? latestLocation,
        DateTimeOffset now,
        int totalPassengerCount)
    {
        var tripStops = trip.TripStops
            .OrderBy(x => x.StopOrder)
            .ToArray();
        var routeStops = trip.Route.RouteStops
            .OrderBy(x => x.StopOrder)
            .ToArray();

        var firstTripStop = tripStops.FirstOrDefault();
        var lastTripStop = tripStops.LastOrDefault();
        var firstRouteStop = routeStops.FirstOrDefault();
        var lastRouteStop = routeStops.LastOrDefault();

        var fromStation = ToStationInfo(firstTripStop, firstRouteStop);
        var toStation = ToStationInfo(lastTripStop, lastRouteStop);
        var currentStop = ResolveCurrentStop(tripStops);
        var nextStop = ResolveNextStop(tripStops, currentStop, latestLocation?.NextStationId);
        var nextRouteStop = ResolveNextRouteStop(routeStops, nextStop, latestLocation?.NextStationId);
        var nextStation = nextStop is null && nextRouteStop is null
            ? null
            : ToStationInfo(nextStop, nextRouteStop);
        var lastStopEvent = ResolveLastStopEvent(tripStops);
        var latestGpsAt = latestLocation?.ReceivedAt;
        var isGpsOnline = latestLocation is not null
            && now - latestLocation.ReceivedAt <= TimeSpan.FromSeconds(GpsOnlineThresholdSeconds);
        var movementStatus = ResolveMovementStatus(trip, tripStops, latestLocation, isGpsOnline, now);
        var dwellCountdown = TripStatusTransitionSupport.ResolveDwellCountdown(trip, currentStop, now);

        return new OperationScheduleItemDto(
            trip.Id,
            "Trip",
            trip.Id,
            trip.TripCode,
            BuildTitle(trip),
            trip.BoatId,
            trip.Boat?.Code,
            trip.Boat?.Name,
            trip.RouteId,
            trip.Route.RouteCode,
            trip.Route.RouteName,
            trip.Route.RouteType,
            trip.TripType,
            ResolveServiceType(trip),
            DistanceFareSupport.UsesDistanceFare(trip.TripType, trip.Route.RouteType),
            trip.CapacitySnapshot,
            totalPassengerCount,
            fromStation.StationId,
            fromStation.StationCode,
            fromStation.LocationName,
            toStation.StationId,
            toStation.StationCode,
            toStation.LocationName,
            trip.OperatingDate,
            trip.DepartureTime,
            ResolveScheduledDepartureAt(trip, currentStop),
            ResolveMinutesUntilDeparture(trip, currentStop, now),
            trip.ArrivalTime,
            trip.TripStatus.ToString(),
            trip.TripStatus == TripStatus.Cancelled
                ? OperationScheduleStates.Cancelled
                : OperationScheduleStates.ReadyForService,
            ToOperationStatus(trip.TripStatus),
            OperationPaymentStages.NotRequired,
            null,
            false,
            trip.DelayMinutes,
            trip.DelayReason,
            trip.AdjustedDepartureTime,
            trip.AdjustedArrivalTime,
            firstTripStop?.ActualDepartureTime ?? firstTripStop?.ActualArrivalTime,
            lastTripStop?.ActualArrivalTime ?? lastTripStop?.ActualDepartureTime,
            now,
            movementStatus,
            currentStop?.StationId,
            currentStop?.Station?.StationCode,
            currentStop?.Station?.StationName,
            nextStation?.StationId,
            nextStation?.StationCode,
            nextStation?.LocationName,
            lastStopEvent.Event,
            lastStopEvent.OccurredAt,
            nextStop?.AdjustedArrivalTime
                ?? nextStop?.AdjustedDepartureTime
                ?? nextStop?.PlannedArrivalTime
                ?? nextStop?.PlannedDepartureTime,
            latestLocation?.Latitude,
            latestLocation?.Longitude,
            latestLocation?.SpeedKmh,
            latestLocation?.Heading,
            latestGpsAt,
            latestLocation?.RemainingDistanceKmToNextStation,
            latestLocation?.RemainingMinutesToNextStation,
            isGpsOnline,
            dwellCountdown,
            BuildStopDtos(tripStops, routeStops),
            toStation.StationId,
            toStation.StationCode,
            toStation.LocationName);
    }

    private static IReadOnlyList<OperationScheduleStopDto> BuildStopDtos(
        IReadOnlyList<TripStop> tripStops,
        IReadOnlyList<RouteStop> routeStops)
    {
        if (tripStops.Count > 0)
        {
            return tripStops
                .OrderBy(x => x.StopOrder)
                .Select(x => new OperationScheduleStopDto(
                    x.Id,
                    x.StopOrder,
                    x.StationId,
                    x.Station?.StationCode,
                    x.Station?.StationName ?? "Chưa xác định",
                    x.PlannedArrivalTime,
                    x.PlannedDepartureTime,
                    x.AdjustedArrivalTime,
                    x.AdjustedDepartureTime,
                    x.ActualArrivalTime,
                    x.ActualDepartureTime,
                    x.StayDurationMinutes,
                    x.StopStatus))
                .ToArray();
        }

        return routeStops
            .OrderBy(x => x.StopOrder)
            .Select(x => new OperationScheduleStopDto(
                TripStopId: null,
                x.StopOrder,
                x.StationId,
                x.Station?.StationCode,
                x.Station?.StationName ?? "Chưa xác định",
                ScheduledArrivalAt: null,
                ScheduledDepartureAt: null,
                AdjustedArrivalAt: null,
                AdjustedDepartureAt: null,
                ActualArrivalAt: null,
                ActualDepartureAt: null,
                StayDurationMinutes: 0,
                StopStatus: null))
            .ToArray();
    }

    private async Task<OperationScheduleAccess> ResolveAccessAsync(
        GetOperationScheduleQuery request,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        if (!_userContext.IsAuthenticated || !_userContext.UserId.HasValue)
        {
            return new OperationScheduleAccess(
                ForcedServiceType: OperationServiceTypes.Booking,
                AllowedStationIds: request.StationId.HasValue ? [request.StationId.Value] : null,
                CanIncludeCancelled: false,
                CanViewLiveTelemetry: false,
                MaxDays: 7);
        }

        var actor = await AuthSupport.GetCurrentUserWithRoleAsync(_context, _userContext, cancellationToken);
        if (AuthSupport.IsCustomer(actor))
        {
            return new OperationScheduleAccess(
                ForcedServiceType: OperationServiceTypes.Booking,
                AllowedStationIds: request.StationId.HasValue ? [request.StationId.Value] : null,
                CanIncludeCancelled: false,
                CanViewLiveTelemetry: false,
                MaxDays: 7);
        }

        if (AuthSupport.IsAdmin(actor) || AuthSupport.IsManager(actor))
        {
            return new OperationScheduleAccess(
                ForcedServiceType: null,
                AllowedStationIds: request.StationId.HasValue ? [request.StationId.Value] : null,
                CanIncludeCancelled: true,
                CanViewLiveTelemetry: true,
                MaxDays: null);
        }

        if (!AuthSupport.IsStaff(actor))
        {
            throw new ForbiddenAccessException();
        }

        if (actor.StaffType == StaffType.OnBoard)
        {
            return new OperationScheduleAccess(
                ForcedServiceType: null,
                AllowedStationIds: request.StationId.HasValue ? [request.StationId.Value] : null,
                CanIncludeCancelled: true,
                CanViewLiveTelemetry: true,
                MaxDays: null);
        }

        var assignedStationIds = await LoadGroundStaffStationIdsAsync(actor.Id, from, to, cancellationToken);
        if (request.StationId.HasValue && !assignedStationIds.Contains(request.StationId.Value))
        {
            throw new ForbiddenAccessException();
        }

        return new OperationScheduleAccess(
            ForcedServiceType: null,
            AllowedStationIds: request.StationId.HasValue ? [request.StationId.Value] : assignedStationIds,
            CanIncludeCancelled: true,
            CanViewLiveTelemetry: true,
            MaxDays: null);
    }

    private async Task<IReadOnlyCollection<Guid>> LoadGroundStaffStationIdsAsync(
        Guid staffUserId,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        var permanentStationIds = await _context.Set<UserStationAssignment>()
            .AsNoTracking()
            .Where(x => x.UserId == staffUserId && x.IsActive)
            .Select(x => x.StationId)
            .ToListAsync(cancellationToken);

        var activeWorkStationIds = await _context.StaffWorkAssignments
            .AsNoTracking()
            .Where(x => x.StaffUserId == staffUserId
                && x.AssignmentType == StaffWorkAssignmentType.Station
                && x.StationId.HasValue
                && x.Status != StaffWorkAssignmentStatus.Cancelled
                && x.Status != StaffWorkAssignmentStatus.Replaced
                && x.StartAt < to
                && x.EndAt >= from)
            .Select(x => x.StationId!.Value)
            .ToListAsync(cancellationToken);

        return permanentStationIds
            .Concat(activeWorkStationIds)
            .Distinct()
            .ToArray();
    }

    private static IQueryable<Trip> ApplyServiceTypeFilter(
        IQueryable<Trip> query,
        string? serviceType)
    {
        return serviceType switch
        {
            OperationServiceTypes.Booking => query.Where(x => x.TripType == TripTypes.Regular
                && (x.Route.RouteType == RouteTypes.Regular || x.Route.RouteType == RouteTypes.SightseeingLoop)),
            OperationServiceTypes.Bus => query.Where(x => x.TripType == TripTypes.Regular
                && x.Route.RouteType == RouteTypes.Regular),
            OperationServiceTypes.Sightseeing => query.Where(x => x.TripType == TripTypes.Regular
                && x.Route.RouteType == RouteTypes.SightseeingLoop),
            OperationServiceTypes.Charter => query.Where(x => x.TripType == TripTypes.Charter
                || x.Route.RouteType == RouteTypes.Charter
                || x.Route.RouteType == RouteTypes.CharterReference),
            _ => query
        };
    }

    private static string? ResolveRouteType(string? routeType)
    {
        if (string.IsNullOrWhiteSpace(routeType))
        {
            return null;
        }

        if (!RouteTypes.IsValid(routeType))
        {
            throw AuthSupport.CreateValidationException(
                nameof(GetOperationScheduleQuery.RouteType),
                $"routeType chi nhan {RouteTypes.Regular}, {RouteTypes.SightseeingLoop}, {RouteTypes.Charter} hoac {RouteTypes.CharterReference}.");
        }

        return RouteTypes.Normalize(routeType);
    }

    private static string? ResolveServiceType(string? serviceType)
    {
        if (string.IsNullOrWhiteSpace(serviceType)
            || string.Equals(serviceType, OperationServiceTypes.All, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var normalized = serviceType.Trim().ToLowerInvariant();
        return normalized switch
        {
            OperationServiceTypes.Booking => OperationServiceTypes.Booking,
            OperationServiceTypes.Bus => OperationServiceTypes.Bus,
            OperationServiceTypes.Sightseeing => OperationServiceTypes.Sightseeing,
            OperationServiceTypes.Charter => OperationServiceTypes.Charter,
            _ => throw AuthSupport.CreateValidationException(
                nameof(GetOperationScheduleQuery.ServiceType),
                "serviceType chi nhan all, booking, bus, sightseeing hoac charter.")
        };
    }

    private static string ResolveServiceType(Trip trip)
    {
        if (trip.TripType == TripTypes.Charter
            || trip.Route.RouteType == RouteTypes.Charter
            || trip.Route.RouteType == RouteTypes.CharterReference)
        {
            return "Charter";
        }

        return trip.Route.RouteType == RouteTypes.SightseeingLoop
            ? "Sightseeing"
            : "Bus";
    }

    private static string BuildTitle(Trip trip) =>
        string.IsNullOrWhiteSpace(trip.Route.RouteName)
            ? trip.TripCode
            : $"{trip.TripCode} - {trip.Route.RouteName}";

    private static StationInfo ToStationInfo(TripStop? tripStop, RouteStop? routeStop)
    {
        var stationId = tripStop?.StationId ?? routeStop?.StationId;
        var stationCode = tripStop?.Station?.StationCode ?? routeStop?.Station?.StationCode;
        var stationName = tripStop?.Station?.StationName ?? routeStop?.Station?.StationName;
        return new StationInfo(stationId, stationCode, stationName ?? "Chưa xác định");
    }

    private static TripStop? ResolveCurrentStop(IReadOnlyList<TripStop> tripStops) =>
        tripStops
            .Where(x => string.Equals(x.StopStatus, TripStopStatuses.Arrived, StringComparison.OrdinalIgnoreCase)
                && x.ActualDepartureTime is null)
            .OrderByDescending(x => x.ActualArrivalTime ?? DateTimeOffset.MinValue)
            .ThenByDescending(x => x.StopOrder)
            .FirstOrDefault();

    private static TripStop? ResolveNextStop(
        IReadOnlyList<TripStop> tripStops,
        TripStop? currentStop,
        Guid? gpsNextStationId)
    {
        if (tripStops.Count == 0)
        {
            return null;
        }

        if (gpsNextStationId.HasValue)
        {
            var gpsNextStop = tripStops
                .OrderBy(x => x.StopOrder)
                .FirstOrDefault(x => x.StationId == gpsNextStationId.Value);
            if (gpsNextStop is not null)
            {
                return gpsNextStop;
            }
        }

        if (currentStop is not null)
        {
            return tripStops
                .Where(x => x.StopOrder > currentStop.StopOrder
                    && !string.Equals(x.StopStatus, TripStopStatuses.Skipped, StringComparison.OrdinalIgnoreCase))
                .OrderBy(x => x.StopOrder)
                .FirstOrDefault();
        }

        return tripStops
            .Where(x => x.ActualArrivalTime is null
                && !string.Equals(x.StopStatus, TripStopStatuses.Skipped, StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.StopOrder)
            .FirstOrDefault();
    }

    private static RouteStop? ResolveNextRouteStop(
        IReadOnlyList<RouteStop> routeStops,
        TripStop? nextStop,
        Guid? gpsNextStationId)
    {
        if (nextStop is not null)
        {
            return routeStops.FirstOrDefault(x => x.StationId == nextStop.StationId);
        }

        return gpsNextStationId.HasValue
            ? routeStops.FirstOrDefault(x => x.StationId == gpsNextStationId.Value)
            : null;
    }

    private static StopEventInfo ResolveLastStopEvent(IReadOnlyList<TripStop> tripStops)
    {
        StopEventInfo latest = new(null, null);
        foreach (var stop in tripStops)
        {
            latest = Max(latest, new StopEventInfo(TripStopStatuses.Arrived, stop.ActualArrivalTime));
            latest = Max(latest, new StopEventInfo(TripStopStatuses.Departed, stop.ActualDepartureTime));
            if (string.Equals(stop.StopStatus, TripStopStatuses.Arriving, StringComparison.OrdinalIgnoreCase))
            {
                latest = Max(latest, new StopEventInfo(TripStopStatuses.Arriving, ResolveAuditTime(stop)));
            }
            else if (string.Equals(stop.StopStatus, TripStopStatuses.Skipped, StringComparison.OrdinalIgnoreCase))
            {
                latest = Max(latest, new StopEventInfo(TripStopStatuses.Skipped, stop.ActualDepartureTime ?? ResolveAuditTime(stop)));
            }
        }

        return latest;
    }

    private static StopEventInfo Max(StopEventInfo left, StopEventInfo right)
    {
        if (right.OccurredAt is null)
        {
            return left;
        }

        if (left.OccurredAt is null || right.OccurredAt > left.OccurredAt)
        {
            return right;
        }

        return left;
    }

    private static DateTimeOffset? ResolveAuditTime(TripStop stop)
    {
        if (stop.LastModified != default)
        {
            return stop.LastModified;
        }

        return stop.Created == default ? null : stop.Created;
    }

    private static DateTimeOffset ResolveScheduledDepartureAt(Trip trip, TripStop? currentStop) =>
        currentStop?.AdjustedDepartureTime
        ?? currentStop?.PlannedDepartureTime
        ?? trip.AdjustedDepartureTime
        ?? trip.DepartureTime;

    private static int? ResolveMinutesUntilDeparture(Trip trip, TripStop? currentStop, DateTimeOffset now)
    {
        if (trip.TripStatus is TripStatus.Cancelled or TripStatus.Completed)
        {
            return null;
        }

        var scheduledDepartureAt = ResolveScheduledDepartureAt(trip, currentStop);
        if (scheduledDepartureAt <= now)
        {
            return null;
        }

        return Math.Max(0, (int)Math.Ceiling((scheduledDepartureAt - now).TotalMinutes));
    }

    private static DateTimeOffset? Max(DateTimeOffset? left, DateTimeOffset? right)
    {
        if (left is null)
        {
            return right;
        }

        if (right is null)
        {
            return left;
        }

        return left > right ? left : right;
    }

    private static string ResolveMovementStatus(
        Trip trip,
        IReadOnlyList<TripStop> tripStops,
        BoatLatestLocation? latestLocation,
        bool isGpsOnline,
        DateTimeOffset now)
    {
        if (trip.TripStatus == TripStatus.Cancelled)
        {
            return OperationStatuses.Cancelled;
        }

        if (trip.TripStatus == TripStatus.Completed)
        {
            return OperationStatuses.Completed;
        }

        if (tripStops.Any(x => string.Equals(x.StopStatus, TripStopStatuses.Arrived, StringComparison.OrdinalIgnoreCase)
            && x.ActualDepartureTime is null))
        {
            return OperationMovementStatuses.AtStation;
        }

        if (tripStops.Any(x => string.Equals(x.StopStatus, TripStopStatuses.Arriving, StringComparison.OrdinalIgnoreCase)))
        {
            return TripStopStatuses.Arriving;
        }

        if (trip.TripStatus == TripStatus.Delayed)
        {
            return OperationStatuses.Delayed;
        }

        if (trip.TripStatus == TripStatus.Boarding)
        {
            return OperationStatuses.Boarding;
        }

        if (trip.TripStatus == TripStatus.InProgress)
        {
            return OperationMovementStatuses.Moving;
        }

        if (isGpsOnline && latestLocation?.TripId == trip.Id)
        {
            return OperationMovementStatuses.Moving;
        }

        return now < trip.DepartureTime
            ? OperationStatuses.Scheduled
            : trip.TripStatus.ToString();
    }

    private static string ToOperationStatus(TripStatus tripStatus) =>
        tripStatus switch
        {
            TripStatus.Scheduled => OperationStatuses.Scheduled,
            TripStatus.Boarding => OperationStatuses.Boarding,
            TripStatus.InProgress => OperationStatuses.InProgress,
            TripStatus.Completed => OperationStatuses.Completed,
            TripStatus.Cancelled => OperationStatuses.Cancelled,
            TripStatus.Delayed => OperationStatuses.Delayed,
            _ => tripStatus.ToString()
        };

    private sealed record StationInfo(
        Guid? StationId,
        string? StationCode,
        string LocationName);

    private sealed record OperationScheduleAccess(
        string? ForcedServiceType,
        IReadOnlyCollection<Guid>? AllowedStationIds,
        bool CanIncludeCancelled,
        bool CanViewLiveTelemetry,
        int? MaxDays);

    private sealed record StopEventInfo(string? Event, DateTimeOffset? OccurredAt);
}

[Authorize(Roles = "Admin,Manager,Staff")]
public sealed record DelayOperationScheduleEntryCommand(
    Guid Id,
    int DelayMinutes,
    string Reason) : IRequest<OperationScheduleItemDto>;

public sealed class DelayOperationScheduleEntryCommandValidator
    : AbstractValidator<DelayOperationScheduleEntryCommand>
{
    public DelayOperationScheduleEntryCommandValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty()
            .WithMessage("Id lịch vận hành không hợp lệ.");
        RuleFor(x => x.DelayMinutes)
            .InclusiveBetween(1, 1440)
            .WithMessage("Số phút delay phải từ 1 đến 1440.");
        RuleFor(x => x.Reason)
            .NotEmpty()
            .WithMessage("Lý do delay là bắt buộc.")
            .MaximumLength(500)
            .WithMessage("Lý do delay không được vượt quá 500 ký tự.");
    }
}

public sealed class DelayOperationScheduleEntryCommandHandler
    : IRequestHandler<DelayOperationScheduleEntryCommand, OperationScheduleItemDto>
{
    public Task<OperationScheduleItemDto> Handle(
        DelayOperationScheduleEntryCommand request,
        CancellationToken cancellationToken) =>
        throw new SaigonWaterbus.Application.Common.Exceptions.NotFoundException("Không tìm thấy lịch vận hành.");
}

public sealed record RefreshOperationScheduleResultDto(
    DateTimeOffset SyncedAt,
    int Count);

public sealed record RefreshOperationScheduleCommand(
    DateTimeOffset From,
    DateTimeOffset To) : IRequest<RefreshOperationScheduleResultDto>;

public sealed class RefreshOperationScheduleCommandHandler
    : IRequestHandler<RefreshOperationScheduleCommand, RefreshOperationScheduleResultDto>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;
    private readonly IOperationScheduleSynchronizer _synchronizer;
    private readonly TimeProvider _timeProvider;

    public RefreshOperationScheduleCommandHandler(
        IApplicationDbContext context,
        IUserContext userContext,
        IOperationScheduleSynchronizer synchronizer,
        TimeProvider timeProvider)
    {
        _context = context;
        _userContext = userContext;
        _synchronizer = synchronizer;
        _timeProvider = timeProvider;
    }

    public async Task<RefreshOperationScheduleResultDto> Handle(
        RefreshOperationScheduleCommand request,
        CancellationToken cancellationToken)
    {
        var from = request.From.ToUniversalTime();
        var to = request.To.ToUniversalTime();
        if (to <= from)
        {
            throw AuthSupport.CreateValidationException(nameof(request.To), "Khoảng thời gian đồng bộ lịch không hợp lệ.");
        }

        var actor = await AuthSupport.GetCurrentUserWithRoleAsync(_context, _userContext, cancellationToken);
        if (!AuthSupport.IsAdmin(actor) && !AuthSupport.IsManager(actor) && !AuthSupport.IsStaff(actor))
        {
            throw new ForbiddenAccessException();
        }

        var count = await _synchronizer.SyncAsync(from, to, cancellationToken);
        return new RefreshOperationScheduleResultDto(_timeProvider.GetUtcNow(), count);
    }
}

internal static class OperationScheduleStates
{
    public const string Request = "Request";
    public const string TentativeHold = "TentativeHold";
    public const string Reserved = "Reserved";
    public const string ReadyForService = "ReadyForService";
    public const string PaymentOverdue = "PaymentOverdue";
    public const string Cancelled = "Cancelled";
}

internal static class OperationPaymentStages
{
    public const string NotRequired = "NotRequired";
    public const string NotCreated = "NotCreated";
    public const string DepositPending = "DepositPending";
    public const string DepositPaid = "DepositPaid";
    public const string FullyPaid = "FullyPaid";
    public const string Failed = "Failed";
}

internal static class OperationServiceTypes
{
    public const string All = "all";
    public const string Booking = "booking";
    public const string Bus = "bus";
    public const string Sightseeing = "sightseeing";
    public const string Charter = "charter";
}

internal static class OperationStatuses
{
    public const string Scheduled = "Scheduled";
    public const string Boarding = "Boarding";
    public const string Departed = "Departed";
    public const string Arrived = "Arrived";
    public const string Delayed = "Delayed";
    public const string InProgress = "InProgress";
    public const string Completed = "Completed";
    public const string Cancelled = "Cancelled";
}

internal static class OperationMovementStatuses
{
    public const string Moving = "Moving";
    public const string AtStation = "AtStation";
}

public sealed class OperationScheduleSynchronizer : IOperationScheduleSynchronizer
{
    private readonly IApplicationDbContext _context;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<OperationScheduleSynchronizer> _logger;

    public OperationScheduleSynchronizer(
        IApplicationDbContext context,
        TimeProvider timeProvider,
        ILogger<OperationScheduleSynchronizer> logger)
    {
        _context = context;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<int> SyncAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("OperationScheduleSynchronizer.SyncAsync called but OperationScheduleEntry has been removed.");
        await Task.CompletedTask;
        return 0;
    }
}
