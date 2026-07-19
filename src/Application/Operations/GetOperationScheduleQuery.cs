using Microsoft.Extensions.Logging;
using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Exceptions;
using SaigonWaterbus.Application.Common.Interfaces;
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
    Guid? FromStationId,
    string? FromStationCode,
    string FromLocation,
    Guid? ToStationId,
    string? ToStationCode,
    string ToLocation,
    DateOnly OperatingDate,
    DateTimeOffset StartAt,
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
    DateTimeOffset? LastStopEventAt,
    DateTimeOffset? NextPlannedArrivalAt,
    decimal? LatestLatitude,
    decimal? LatestLongitude,
    decimal? LatestSpeedKmh,
    int? LatestHeading,
    DateTimeOffset? LatestGpsAt,
    decimal? RemainingDistanceKmToNextStation,
    int? RemainingMinutesToNextStation,
    bool IsGpsOnline);

[Authorize(Roles = "Admin,Manager,Staff")]
public sealed record GetOperationScheduleQuery(
    DateTimeOffset From,
    DateTimeOffset To,
    bool IncludeCancelled = false) : IRequest<IReadOnlyList<OperationScheduleItemDto>>;

public sealed class GetOperationScheduleQueryHandler
    : IRequestHandler<GetOperationScheduleQuery, IReadOnlyList<OperationScheduleItemDto>>
{
    private const int GpsOnlineThresholdSeconds = 60;

    private readonly IApplicationDbContext _context;
    private readonly TimeProvider _timeProvider;

    public GetOperationScheduleQueryHandler(
        IApplicationDbContext context,
        TimeProvider timeProvider)
    {
        _context = context;
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

        var query = _context.Set<Trip>()
            .AsNoTracking()
            .Include(x => x.Boat)
            .Include(x => x.Route)
                .ThenInclude(x => x.RouteStops)
                    .ThenInclude(x => x.Station)
            .Include(x => x.TripStops)
                .ThenInclude(x => x.Station)
            .Where(x => x.DepartureTime < to && x.ArrivalTime >= from);

        if (!request.IncludeCancelled)
        {
            query = query.Where(x => x.TripStatus != TripStatus.Cancelled);
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

        var now = _timeProvider.GetUtcNow();
        return trips
            .Select(trip =>
            {
                latestLocations.TryGetValue(trip.BoatId ?? Guid.Empty, out var latestLocation);
                var tripLatestLocation = latestLocation?.TripId == trip.Id ? latestLocation : null;
                return ToOperationScheduleItem(trip, tripLatestLocation, now);
            })
            .ToArray();
    }

    private static OperationScheduleItemDto ToOperationScheduleItem(
        Trip trip,
        BoatLatestLocation? latestLocation,
        DateTimeOffset now)
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
        var lastStopEventAt = ResolveLastStopEventAt(tripStops);
        var latestGpsAt = latestLocation?.ReceivedAt;
        var isGpsOnline = latestLocation is not null
            && now - latestLocation.ReceivedAt <= TimeSpan.FromSeconds(GpsOnlineThresholdSeconds);
        var movementStatus = ResolveMovementStatus(trip, tripStops, latestLocation, isGpsOnline, now);

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
            fromStation.StationId,
            fromStation.StationCode,
            fromStation.LocationName,
            toStation.StationId,
            toStation.StationCode,
            toStation.LocationName,
            trip.OperatingDate,
            trip.DepartureTime,
            trip.ArrivalTime,
            trip.TripStatus.ToString(),
            trip.TripStatus == TripStatus.Cancelled
                ? OperationScheduleStates.Cancelled
                : OperationScheduleStates.ReadyForService,
            ToOperationStatus(trip.TripStatus),
            OperationPaymentStages.NotRequired,
            null,
            false,
            0,
            null,
            null,
            null,
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
            lastStopEventAt,
            nextStop?.PlannedArrivalTime ?? nextStop?.PlannedDepartureTime,
            latestLocation?.Latitude,
            latestLocation?.Longitude,
            latestLocation?.SpeedKmh,
            latestLocation?.Heading,
            latestGpsAt,
            latestLocation?.RemainingDistanceKmToNextStation,
            latestLocation?.RemainingMinutesToNextStation,
            isGpsOnline);
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

    private static DateTimeOffset? ResolveLastStopEventAt(IReadOnlyList<TripStop> tripStops)
    {
        DateTimeOffset? lastEventAt = null;
        foreach (var stop in tripStops)
        {
            lastEventAt = Max(lastEventAt, stop.ActualArrivalTime);
            lastEventAt = Max(lastEventAt, stop.ActualDepartureTime);
        }

        return lastEventAt;
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
