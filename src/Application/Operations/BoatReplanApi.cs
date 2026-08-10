using FluentValidation.Results;
using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Bookings;
using SaigonWaterbus.Application.Common.Exceptions;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Application.Notifications;
using SaigonWaterbus.Application.Trips;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.Operations;

public sealed record BoatReplanCandidateDto(
    Guid BoatId,
    string BoatCode,
    string BoatName,
    int Capacity,
    DateTimeOffset EarliestAvailableAt,
    bool CanTakeSourceTrip,
    bool IsRecommended,
    IReadOnlyList<Guid> ConflictingTripIds,
    string? Reason);

public sealed record BoatReplanAffectedTripDto(
    Guid TripId,
    string TripCode,
    Guid? CurrentBoatId,
    string? CurrentBoatCode,
    string TripStatus,
    DateTimeOffset CurrentDepartureAt,
    DateTimeOffset CurrentArrivalAt,
    int TotalPassengerCount,
    int OnboardPassengerCount,
    int AlightedPassengerCount,
    bool HasPassengers,
    bool HasBoatConflict,
    string RecommendedAction,
    Guid? RecommendedBoatId,
    DateTimeOffset? ProposedDepartureAt,
    DateTimeOffset? ProposedArrivalAt,
    int ProposedDelayMinutes,
    string Reason);

public sealed record BoatReplanPreviewDto(
    Guid? IncidentId,
    Guid SourceTripId,
    string SourceTripCode,
    Guid OriginalBoatId,
    string OriginalBoatCode,
    DateTimeOffset ReplacementAvailableAt,
    DateTimeOffset CalculatedAt,
    IReadOnlyList<BoatReplanCandidateDto> Candidates,
    IReadOnlyList<BoatReplanAffectedTripDto> AffectedTrips);

public sealed record PreviewBoatReplanQuery(
    Guid? IncidentId,
    Guid? SourceTripId,
    DateTimeOffset? ReplacementAvailableAt = null,
    Guid? ReplacementBoatId = null) : IRequest<BoatReplanPreviewDto>;

public sealed record BoatReplanTripDecision(
    Guid TripId,
    string Action,
    Guid? ReplacementBoatId = null,
    DateTimeOffset? DepartureAt = null,
    string? Note = null);

public sealed record ConfirmBoatReplanCommand(
    Guid? IncidentId,
    Guid SourceTripId,
    Guid ReplacementBoatId,
    DateTimeOffset ReplacementAvailableAt,
    IReadOnlyList<BoatReplanTripDecision> Decisions,
    string? Reason = null) : IRequest<BoatReplanPreviewDto>;

[Authorize(Roles = "Admin,Manager")]
public sealed class PreviewBoatReplanQueryHandler
    : IRequestHandler<PreviewBoatReplanQuery, BoatReplanPreviewDto>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;
    private readonly TimeProvider _timeProvider;

    public PreviewBoatReplanQueryHandler(
        IApplicationDbContext context,
        IUserContext userContext,
        TimeProvider timeProvider)
    {
        _context = context;
        _userContext = userContext;
        _timeProvider = timeProvider;
    }

    public async Task<BoatReplanPreviewDto> Handle(
        PreviewBoatReplanQuery request,
        CancellationToken cancellationToken)
    {
        await EnsureCanReplanAsync(cancellationToken);
        var source = await BoatReplanSupport.LoadSourceTripAsync(
            _context,
            request.IncidentId,
            request.SourceTripId,
            cancellationToken);
        var availableAt = request.ReplacementAvailableAt
            ?? source.Incident?.ReplacementEstimatedResumeAt
            ?? source.Trip.AdjustedArrivalTime
            ?? source.Trip.ArrivalTime;

        return await BoatReplanSupport.BuildPreviewAsync(
            _context,
            source,
            request.IncidentId,
            availableAt,
            request.ReplacementBoatId,
            _timeProvider.GetUtcNow(),
            cancellationToken);
    }

    private async Task EnsureCanReplanAsync(CancellationToken cancellationToken)
    {
        var actor = await AuthSupport.GetCurrentUserWithRoleAsync(
            _context,
            _userContext,
            cancellationToken);
        if (!AuthSupport.IsAdmin(actor) && !AuthSupport.IsManager(actor))
        {
            throw new ForbiddenAccessException();
        }
    }
}

[Authorize(Roles = "Admin,Manager")]
public sealed class ConfirmBoatReplanCommandHandler
    : IRequestHandler<ConfirmBoatReplanCommand, BoatReplanPreviewDto>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;
    private readonly TimeProvider _timeProvider;
    private readonly INotificationRealtimeNotifier _notificationRealtimeNotifier;
    private readonly IPushNotificationSender _pushNotificationSender;

    public ConfirmBoatReplanCommandHandler(
        IApplicationDbContext context,
        IUserContext userContext,
        TimeProvider timeProvider,
        INotificationRealtimeNotifier? notificationRealtimeNotifier = null,
        IPushNotificationSender? pushNotificationSender = null)
    {
        _context = context;
        _userContext = userContext;
        _timeProvider = timeProvider;
        _notificationRealtimeNotifier = notificationRealtimeNotifier ?? NullNotificationRealtimeNotifier.Instance;
        _pushNotificationSender = pushNotificationSender ?? NullPushNotificationSender.Instance;
    }

    public async Task<BoatReplanPreviewDto> Handle(
        ConfirmBoatReplanCommand request,
        CancellationToken cancellationToken)
    {
        var actor = await AuthSupport.GetCurrentUserWithRoleAsync(
            _context,
            _userContext,
            cancellationToken);
        if (!AuthSupport.IsAdmin(actor) && !AuthSupport.IsManager(actor))
        {
            throw new ForbiddenAccessException();
        }

        if (request.ReplacementAvailableAt <= DateTimeOffset.MinValue)
        {
            throw new ValidationException([new ValidationFailure(
                nameof(request.ReplacementAvailableAt),
                "replacementAvailableAt không hợp lệ.")]);
        }

        var source = await BoatReplanSupport.LoadSourceTripAsync(
            _context,
            request.IncidentId,
            request.SourceTripId,
            cancellationToken);
        var preview = await BoatReplanSupport.BuildPreviewAsync(
            _context,
            source,
            request.IncidentId,
            request.ReplacementAvailableAt,
            request.ReplacementBoatId,
            _timeProvider.GetUtcNow(),
            cancellationToken);

        var selectedCandidate = preview.Candidates
            .SingleOrDefault(x => x.BoatId == request.ReplacementBoatId);
        if (selectedCandidate is null || !selectedCandidate.CanTakeSourceTrip)
        {
            throw new ValidationException([new ValidationFailure(
                nameof(request.ReplacementBoatId),
                "Tàu thay thế không phù hợp hoặc đang xung đột lịch với chuyến cần xử lý.")]);
        }

        var decisionsByTripId = request.Decisions
            .GroupBy(x => x.TripId)
            .ToDictionary(x => x.Key, x => x.Last());
        var allowedTripIds = preview.AffectedTrips
            .Select(x => x.TripId)
            .ToHashSet();
        var invalidDecision = request.Decisions.FirstOrDefault(x =>
            x.TripId == request.SourceTripId
            || !allowedTripIds.Contains(x.TripId));
        if (invalidDecision is not null)
        {
            throw new ValidationException([new ValidationFailure(
                nameof(request.Decisions),
                "Decision chỉ được áp dụng cho các chuyến nằm trong affectedTrips và không bao gồm sourceTripId.")]);
        }

        var createdNotifications = new List<Notification>();
        var changedTripIds = new HashSet<Guid>();
        var originalBoatIds = new Dictionary<Guid, Guid?>();
        var now = _timeProvider.GetUtcNow();

        await _context.ExecuteInTransactionAsync(async ct =>
        {
            var freshSource = await BoatReplanSupport.LoadSourceTripAsync(
                _context,
                request.IncidentId,
                request.SourceTripId,
                ct);
            var replacementBoat = await _context.Set<Boat>()
                .SingleOrDefaultAsync(x => x.Id == request.ReplacementBoatId, ct)
                ?? throw new NotFoundException("Không tìm thấy tàu thay thế.");

            await BoatReplanSupport.ReplaceTripBoatAsync(
                _context,
                freshSource.Trip,
                replacementBoat,
                ct);
            originalBoatIds[freshSource.Trip.Id] = freshSource.OriginalBoatId;
            changedTripIds.Add(freshSource.Trip.Id);

            foreach (var affected in preview.AffectedTrips)
            {
                var decision = decisionsByTripId.GetValueOrDefault(affected.TripId)
                    ?? BuildAutomaticDecision(affected);
                if (decision is null)
                {
                    continue;
                }

                var trip = await BoatReplanSupport.LoadTripForMutationAsync(
                    _context,
                    affected.TripId,
                    ct);
                originalBoatIds[trip.Id] = trip.BoatId;
                await ApplyDecisionAsync(trip, decision, ct);
                changedTripIds.Add(trip.Id);
            }

            foreach (var tripId in changedTripIds)
            {
                var trip = await BoatReplanSupport.LoadTripForMutationAsync(
                    _context,
                    tripId,
                    ct);
                var oldBoatId = originalBoatIds.GetValueOrDefault(tripId);
                var oldStatus = trip.TripStatus;
                var reason = request.Reason?.Trim();
                if (trip.Id == freshSource.Trip.Id && trip.TripStatus == TripStatus.Scheduled)
                {
                    trip.StatusNote = reason ?? "Đã điều phối tàu thay thế.";
                }

                createdNotifications.AddRange(await BoatReplanSupport.AddReplanNotificationsAsync(
                    _context,
                    trip,
                    oldBoatId,
                    oldStatus,
                    now,
                    reason,
                    ct));
            }

            await _context.SaveChangesAsync(ct);
        }, cancellationToken);

        await NotificationSupport.PublishCreatedAsync(
            _notificationRealtimeNotifier,
            _pushNotificationSender,
            createdNotifications,
            cancellationToken);

        var finalSource = await BoatReplanSupport.LoadSourceTripAsync(
            _context,
            request.IncidentId,
            request.SourceTripId,
            cancellationToken);
        return await BoatReplanSupport.BuildPreviewAsync(
            _context,
            finalSource,
            request.IncidentId,
            request.ReplacementAvailableAt,
            request.ReplacementBoatId,
            now,
            cancellationToken);
    }

    private async Task ApplyDecisionAsync(
        Trip trip,
        BoatReplanTripDecision decision,
        CancellationToken cancellationToken)
    {
        var action = decision.Action.Trim();
        switch (action)
        {
            case "Keep":
                return;

            case "ReplaceBoat":
                if (!decision.ReplacementBoatId.HasValue)
                {
                    throw new ValidationException([new ValidationFailure(
                        nameof(decision.ReplacementBoatId),
                        $"Trip {trip.TripCode} cần replacementBoatId.")]);
                }

                var replacementBoat = await _context.Set<Boat>()
                    .SingleOrDefaultAsync(x => x.Id == decision.ReplacementBoatId.Value, cancellationToken)
                    ?? throw new NotFoundException("Không tìm thấy tàu thay thế cho affected trip.");
                await BoatReplanSupport.ReplaceTripBoatAsync(
                    _context,
                    trip,
                    replacementBoat,
                    cancellationToken);
                return;

            case "Delay":
                if (!decision.DepartureAt.HasValue)
                {
                    throw new ValidationException([new ValidationFailure(
                        nameof(decision.DepartureAt),
                        $"Trip {trip.TripCode} cần departureAt khi action=Delay.")]);
                }

                var currentDeparture = TripDelaySupport.ResolveAdjustedDeparture(trip);
                if (decision.DepartureAt.Value < currentDeparture)
                {
                    throw new ValidationException([new ValidationFailure(
                        nameof(decision.DepartureAt),
                        $"departureAt của trip {trip.TripCode} không được sớm hơn giờ hiện tại.")]);
                }

                var delayMinutes = (int)Math.Ceiling(
                    (decision.DepartureAt.Value - trip.DepartureTime).TotalMinutes);
                TripDelaySupport.ApplyTotalDelayToFutureTrip(
                    trip,
                    Math.Max(trip.DelayMinutes, delayMinutes),
                    decision.Note ?? "Chuyến bị ảnh hưởng bởi phương án điều phối tàu.");
                return;

            case "Cancel":
                trip.TripStatus = TripStatus.Cancelled;
                trip.StatusNote = decision.Note?.Trim()
                    ?? "Chuyến bị hủy do không có tàu thay thế phù hợp.";
                return;

            case "HoldDelayed":
                trip.TripStatus = TripStatus.Delayed;
                trip.DelayReason = decision.Note?.Trim()
                    ?? "Đang chờ Manager điều tàu thay thế.";
                trip.StatusNote = trip.DelayReason;
                return;

            default:
                throw new ValidationException([new ValidationFailure(
                    nameof(decision.Action),
                    "action chỉ nhận Keep, ReplaceBoat, Delay, HoldDelayed hoặc Cancel.")]);
        }
    }

    private static BoatReplanTripDecision? BuildAutomaticDecision(
        BoatReplanAffectedTripDto affected)
    {
        if (affected.HasBoatConflict && affected.ProposedDepartureAt.HasValue)
        {
            return new BoatReplanTripDecision(
                affected.TripId,
                "Delay",
                DepartureAt: affected.ProposedDepartureAt,
                Note: "Tự động dời giờ do tàu thay thế đang bàn giao chuyến trước.");
        }

        if (!affected.HasPassengers)
        {
            return new BoatReplanTripDecision(
                affected.TripId,
                "Cancel",
                Note: "Tự động hủy vì không có khách và không còn tàu phù hợp.");
        }

        if (affected.RecommendedBoatId.HasValue)
        {
            return new BoatReplanTripDecision(
                affected.TripId,
                "ReplaceBoat",
                affected.RecommendedBoatId,
                Note: "Tự động đề xuất tàu thay thế cho chuyến có khách.");
        }

        return new BoatReplanTripDecision(
            affected.TripId,
            "HoldDelayed",
            Note: "Chuyến có khách nhưng chưa có tàu thay thế phù hợp; cần Manager xử lý.");
    }
}

internal sealed record BoatReplanSource(
    Guid? IncidentId,
    Trip Trip,
    Incident? Incident,
    Guid OriginalBoatId,
    string OriginalBoatCode);

internal static class BoatReplanSupport
{
    public static async Task<BoatReplanSource> LoadSourceTripAsync(
        IApplicationDbContext context,
        Guid? incidentId,
        Guid? sourceTripId,
        CancellationToken cancellationToken)
    {
        Incident? incident = null;
        if (incidentId.HasValue)
        {
            incident = await context.Set<Incident>()
                .AsNoTracking()
                .Include(x => x.Trip)
                .SingleOrDefaultAsync(x => x.Id == incidentId.Value, cancellationToken)
                ?? throw new NotFoundException("Không tìm thấy incident.");
            sourceTripId ??= incident.TripId;
        }

        if (!sourceTripId.HasValue)
        {
            throw new ValidationException([new ValidationFailure(
                nameof(sourceTripId),
                "Cần incidentId có trip hoặc sourceTripId.")]);
        }

        var trip = await context.Set<Trip>()
            .Include(x => x.Boat)
            .Include(x => x.Route)
                .ThenInclude(x => x.RouteStops)
                    .ThenInclude(x => x.Station)
            .Include(x => x.TripStops)
                .ThenInclude(x => x.Station)
            .Include(x => x.TripSeats)
                .ThenInclude(x => x.Seat)
            .SingleOrDefaultAsync(x => x.Id == sourceTripId.Value, cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy source trip.");

        if (!trip.BoatId.HasValue || trip.Boat is null)
        {
            throw new ValidationException([new ValidationFailure(
                nameof(sourceTripId),
                "Source trip chưa được gán tàu.")]);
        }

        return new BoatReplanSource(
            incidentId,
            trip,
            incident,
            trip.BoatId.Value,
            trip.Boat.Code);
    }

    public static async Task<BoatReplanPreviewDto> BuildPreviewAsync(
        IApplicationDbContext context,
        BoatReplanSource source,
        Guid? incidentId,
        DateTimeOffset replacementAvailableAt,
        Guid? selectedReplacementBoatId,
        DateTimeOffset calculatedAt,
        CancellationToken cancellationToken)
    {
        var sourceTrip = source.Trip;
        var candidateBoats = await context.Set<Boat>()
            .AsNoTracking()
            .Where(x => x.Id != source.OriginalBoatId
                && x.Status == BoatStatus.Active
                && x.ServiceType == BoatServiceType.Passenger
                && x.SeatsConfigured)
            .OrderBy(x => x.Code)
            .ToListAsync(cancellationToken);

        var candidateIds = candidateBoats.Select(x => x.Id).ToArray();
        var relevantBoatIds = candidateIds.Append(source.OriginalBoatId).ToArray();
        var sameDayTrips = await context.Set<Trip>()
            .AsNoTracking()
            .Include(x => x.Route)
                .ThenInclude(x => x.RouteStops)
                    .ThenInclude(x => x.Station)
            .Where(x => x.OperatingDate == sourceTrip.OperatingDate
                && x.BoatId.HasValue
                && relevantBoatIds.Contains(x.BoatId.Value)
                && x.Id != sourceTrip.Id
                && x.TripStatus != TripStatus.Cancelled
                && x.TripStatus != TripStatus.Completed)
            .OrderBy(x => x.DepartureTime)
            .ToListAsync(cancellationToken);

        var candidates = candidateBoats
            .Select(boat => BuildCandidate(
                boat,
                sourceTrip,
                sameDayTrips.Where(x => x.BoatId == boat.Id).ToList(),
                replacementAvailableAt))
            .OrderByDescending(x => x.CanTakeSourceTrip)
            .ThenBy(x => x.ConflictingTripIds.Count)
            .ThenBy(x => x.EarliestAvailableAt)
            .ThenBy(x => x.BoatCode)
            .ToList();
        if (candidates.Count > 0)
        {
            var recommended = selectedReplacementBoatId.HasValue
                ? candidates.FirstOrDefault(x => x.BoatId == selectedReplacementBoatId.Value)
                : candidates.FirstOrDefault(x => x.CanTakeSourceTrip);
            if (recommended is not null && !recommended.CanTakeSourceTrip)
            {
                recommended = null;
            }
            if (recommended is not null)
            {
                candidates = candidates
                    .Select(x => x with { IsRecommended = x.BoatId == recommended.BoatId })
                    .ToList();
            }
        }

        var affectedTrips = sameDayTrips
            .Where(x => x.BoatId == source.OriginalBoatId
                && x.DepartureTime > sourceTrip.DepartureTime
                || x.BoatId.HasValue
                    && candidates.Any(candidate => candidate.BoatId == x.BoatId
                        && candidate.ConflictingTripIds.Contains(x.Id)))
            .GroupBy(x => x.Id)
            .Select(x => x.First())
            .OrderBy(x => x.DepartureTime)
            .ToList();

        var tripIds = affectedTrips.Select(x => x.Id).Append(sourceTrip.Id).ToArray();
        var totalCounts = await LoadPassengerCountsAsync(context, tripIds, cancellationToken);
        var onboardCounts = await TripPassengerCountSupport.LoadOnboardPassengerCountsByTripIdAsync(
            context,
            tripIds,
            cancellationToken);
        var alightedCounts = await TripPassengerCountSupport.LoadAlightedPassengerCountsByTripIdAsync(
            context,
            tripIds,
            cancellationToken);

        var recommendedCandidate = candidates.FirstOrDefault(x => x.IsRecommended);
        var affectedDtos = affectedTrips
            .Select(trip =>
            {
                var total = totalCounts.GetValueOrDefault(trip.Id);
                var onboard = onboardCounts.GetValueOrDefault(trip.Id);
                var alighted = alightedCounts.GetValueOrDefault(trip.Id);
                var conflict = recommendedCandidate is not null
                    && recommendedCandidate.ConflictingTripIds.Contains(trip.Id)
                    && trip.BoatId == recommendedCandidate.BoatId;
                var originalBoatAffected = trip.BoatId == source.OriginalBoatId;
                var proposedDeparture = conflict
                    ? replacementAvailableAt.AddMinutes(TripDelaySupport.TurnaroundBufferMinutes)
                    : (DateTimeOffset?)null;
                if (proposedDeparture.HasValue
                    && proposedDeparture.Value < TripDelaySupport.ResolveAdjustedDeparture(trip))
                {
                    proposedDeparture = TripDelaySupport.ResolveAdjustedDeparture(trip);
                }

                var delayMinutes = proposedDeparture.HasValue
                    ? Math.Max(0, (int)Math.Ceiling(
                        (proposedDeparture.Value - trip.DepartureTime).TotalMinutes))
                    : 0;
                var recommendedBoatForTrip = recommendedCandidate is not null
                    && !recommendedCandidate.ConflictingTripIds.Contains(trip.Id)
                    ? recommendedCandidate.BoatId
                    : (Guid?)null;
                return new BoatReplanAffectedTripDto(
                    trip.Id,
                    trip.TripCode,
                    trip.BoatId,
                    trip.Boat?.Code,
                    trip.TripStatus.ToString(),
                    TripDelaySupport.ResolveAdjustedDeparture(trip),
                    TripDelaySupport.ResolveAdjustedArrival(trip),
                    total,
                    onboard,
                    alighted,
                    total > 0,
                    originalBoatAffected || conflict,
                    originalBoatAffected ? "ReplaceBoat" : conflict ? "DelayOrReplaceBoat" : "Review",
                    originalBoatAffected || conflict ? recommendedBoatForTrip : null,
                    proposedDeparture,
                    proposedDeparture.HasValue
                        ? TripDelaySupport.ResolveAdjustedArrival(trip).AddMinutes(delayMinutes)
                        : null,
                    delayMinutes,
                    originalBoatAffected
                        ? "Tàu hiện tại bị ảnh hưởng bởi sự cố/bảo trì."
                        : "Tàu thay thế đang bị xung đột với lịch chuyến này.");
            })
            .ToList();

        return new BoatReplanPreviewDto(
            incidentId,
            sourceTrip.Id,
            sourceTrip.TripCode,
            source.OriginalBoatId,
            source.OriginalBoatCode,
            replacementAvailableAt,
            calculatedAt,
            candidates,
            affectedDtos);
    }

    public static async Task<Trip> LoadTripForMutationAsync(
        IApplicationDbContext context,
        Guid tripId,
        CancellationToken cancellationToken) =>
        await context.Set<Trip>()
            .Include(x => x.Boat)
            .Include(x => x.Route)
                .ThenInclude(x => x.RouteStops)
                    .ThenInclude(x => x.Station)
            .Include(x => x.TripStops)
                .ThenInclude(x => x.Station)
            .Include(x => x.TripSeats)
                .ThenInclude(x => x.Seat)
            .SingleOrDefaultAsync(x => x.Id == tripId, cancellationToken)
        ?? throw new NotFoundException("Không tìm thấy affected trip.");

    public static async Task ReplaceTripBoatAsync(
        IApplicationDbContext context,
        Trip trip,
        Boat boat,
        CancellationToken cancellationToken)
    {
        if (trip.TripStatus is TripStatus.Completed or TripStatus.Cancelled)
        {
            throw new ValidationException([new ValidationFailure(
                nameof(trip.Id),
                $"Không thể thay tàu cho trip {trip.TripCode} đã hoàn tất hoặc bị hủy.")]);
        }

        if (trip.BoatId == boat.Id)
        {
            return;
        }

        if (boat.Status != BoatStatus.Active
            || boat.ServiceType != BoatServiceType.Passenger
            || !boat.SeatsConfigured
            || !BoatRouteCompatibilitySupport.IsCompatible(trip.Route.RouteType, boat.SeatSetupType))
        {
            throw new ValidationException([new ValidationFailure(
                nameof(boat.Id),
                "Tàu thay thế phải Active, là tàu Passenger, đã setup ghế và tương thích route.")]);
        }

        var conflictingTrip = await context.Set<Trip>()
            .AsNoTracking()
            .Where(x => x.Id != trip.Id
                && x.BoatId == boat.Id
                && x.OperatingDate == trip.OperatingDate
                && x.TripStatus != TripStatus.Cancelled
                && x.TripStatus != TripStatus.Completed
                && x.DepartureTime < TripDelaySupport.ResolveAdjustedArrival(trip)
                && TripDelaySupport.ResolveAdjustedDeparture(x) < TripDelaySupport.ResolveAdjustedArrival(trip))
            .OrderBy(x => x.DepartureTime)
            .FirstOrDefaultAsync(cancellationToken);
        if (conflictingTrip is not null)
        {
            throw new ValidationException([new ValidationFailure(
                nameof(boat.Id),
                $"Tàu {boat.Code} đang xung đột với trip {conflictingTrip.TripCode}.")]);
        }

        var newBoatSeats = await context.Set<Seat>()
            .Where(x => x.BoatId == boat.Id && x.IsActive)
            .OrderBy(x => x.Deck)
            .ThenBy(x => x.Row)
            .ThenBy(x => x.Column)
            .ToListAsync(cancellationToken);
        var requiredSeatCodes = await context.Set<BookingPassenger>()
            .Include(x => x.TripSeat)
                .ThenInclude(x => x!.Seat)
            .Where(x => x.TripId == trip.Id && x.TripSeatId.HasValue)
            .Select(x => x.TripSeat!.Seat.Code)
            .Distinct()
            .ToListAsync(cancellationToken);
        var newSeatCodes = newBoatSeats
            .Select(x => x.Code)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missingSeat = requiredSeatCodes.FirstOrDefault(x => !newSeatCodes.Contains(x));
        if (missingSeat is not null)
        {
            throw new ValidationException([new ValidationFailure(
                nameof(boat.Id),
                $"Tàu {boat.Code} thiếu mã ghế {missingSeat} đang có khách trên trip.")]);
        }

        var newTripSeats = newBoatSeats
            .Select(seat => new TripSeat
            {
                TripId = trip.Id,
                SeatId = seat.Id,
                Status = TripSeat.StatusAvailable,
                Price = null
            })
            .ToList();
        var newTripSeatsByCode = newTripSeats
            .Join(newBoatSeats, x => x.SeatId, x => x.Id, (tripSeat, seat) => new { tripSeat, seat.Code })
            .ToDictionary(x => x.Code, x => x.tripSeat, StringComparer.OrdinalIgnoreCase);
        var passengers = await context.Set<BookingPassenger>()
            .Include(x => x.TripSeat)
                .ThenInclude(x => x!.Seat)
            .Where(x => x.TripId == trip.Id && x.TripSeatId.HasValue)
            .ToListAsync(cancellationToken);
        foreach (var passenger in passengers)
        {
            var code = passenger.TripSeat?.Seat?.Code;
            if (code is not null && newTripSeatsByCode.TryGetValue(code, out var newTripSeat))
            {
                passenger.TripSeatId = newTripSeat.Id;
                passenger.TripSeat = newTripSeat;
            }
        }

        context.Set<TripSeat>().AddRange(newTripSeats);
        context.Set<TripSeat>().RemoveRange(trip.TripSeats.ToList());
        trip.BoatId = boat.Id;
        trip.Boat = boat;
        trip.CapacitySnapshot = newBoatSeats.Count;
    }

    public static async Task<IReadOnlyList<Notification>> AddReplanNotificationsAsync(
        IApplicationDbContext context,
        Trip trip,
        Guid? oldBoatId,
        TripStatus oldStatus,
        DateTimeOffset now,
        string? reason,
        CancellationToken cancellationToken)
    {
        var created = new List<Notification>();
        var tripText = $"Chuyến {trip.TripCode}";
        var body = string.IsNullOrWhiteSpace(reason)
            ? $"{tripText} đã được điều phối lại. Tàu hiện tại: {trip.Boat?.Code ?? "chưa gán"}."
            : $"{tripText} đã được điều phối lại. {reason.Trim()} Tàu hiện tại: {trip.Boat?.Code ?? "chưa gán"}.";

        var customerIds = await context.Set<Booking>()
            .AsNoTracking()
            .Where(x => x.UserId.HasValue
                && x.BookingStatus == BookingStatus.Confirmed
                && (x.TripId == trip.Id
                    || x.ReturnTripId == trip.Id
                    || (trip.SourceBookingId.HasValue && x.Id == trip.SourceBookingId.Value)))
            .Select(x => new { x.Id, UserId = x.UserId!.Value })
            .ToListAsync(cancellationToken);
        foreach (var customer in customerIds)
        {
            var notification = new Notification
            {
                UserId = customer.UserId,
                Title = "Chuyến đi đã được điều phối lại",
                Body = body,
                Type = NotificationTypes.TripReplanned,
                RelatedEntityType = NotificationRelatedEntityTypes.Booking,
                RelatedEntityId = customer.Id,
                CreatedAt = now
            };
            context.Set<Notification>().Add(notification);
            created.Add(notification);
        }

        var operationalIds = await context.Set<User>()
            .AsNoTracking()
            .Where(x => x.Status == UserStatus.Active
                && (x.Role.Code == Roles.AdminCode || x.Role.Code == Roles.ManagerCode))
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);
        var requiresManagerAction = trip.TripStatus == TripStatus.Delayed
            && trip.DelayMinutes == 0
            && trip.StatusNote?.Contains("Manager", StringComparison.OrdinalIgnoreCase) == true;
        created.AddRange(AddNotifications(
            context,
            operationalIds,
            requiresManagerAction ? "Cần điều phối thêm tàu" : "Đã điều phối lại chuyến",
            body,
            requiresManagerAction
                ? NotificationTypes.OperationsReplanRequired
                : NotificationTypes.OperationsTripReplanned,
            trip.Id,
            now));

        var boatIds = new[] { oldBoatId, trip.BoatId }
            .Where(x => x.HasValue)
            .Select(x => x!.Value)
            .Distinct()
            .ToArray();
        var staffIds = await context.Set<StaffWorkAssignment>()
            .AsNoTracking()
            .Where(x => boatIds.Contains(x.BoatId ?? Guid.Empty)
                && x.AssignmentType == StaffWorkAssignmentType.Boat
                && x.Status == StaffWorkAssignmentStatus.Scheduled
                && x.StaffUser.Status == UserStatus.Active
                && x.StaffUser.StaffType == StaffType.OnBoard
                && x.StartAt < trip.ArrivalTime
                && trip.DepartureTime < x.EndAt)
            .Select(x => x.StaffUserId)
            .Distinct()
            .ToListAsync(cancellationToken);
        created.AddRange(AddNotifications(
            context,
            staffIds,
            "Chuyến được điều phối lại",
            body,
            NotificationTypes.StaffTripReplanned,
            trip.Id,
            now));

        if (trip.TripStatus != oldStatus)
        {
            created.AddRange(await NotificationSupport.AddTripStatusChangedNotificationsAsync(
                context,
                trip,
                oldStatus,
                now,
                cancellationToken));
            created.AddRange(await StaffTripNotificationSupport.AddManagementTripStatusNotificationsAsync(
                context,
                trip,
                oldStatus,
                now,
                cancellationToken));
            created.AddRange(await StaffTripNotificationSupport.AddTripStatusChangedNotificationsAsync(
                context,
                trip,
                oldStatus,
                now,
                cancellationToken));
        }

        return created;
    }

    private static BoatReplanCandidateDto BuildCandidate(
        Boat boat,
        Trip sourceTrip,
        IReadOnlyList<Trip> boatTrips,
        DateTimeOffset replacementAvailableAt)
    {
        var conflicts = boatTrips
            .Where(x => x.DepartureTime < replacementAvailableAt
                && TripDelaySupport.ResolveAdjustedArrival(x) > sourceTrip.DepartureTime)
            .ToList();
        var earliest = conflicts
            .Select(x => TripDelaySupport.ResolveAdjustedArrival(x).AddMinutes(TripDelaySupport.TurnaroundBufferMinutes))
            .DefaultIfEmpty(replacementAvailableAt)
            .Max();
        var canTake = !boatTrips.Any(x =>
            x.DepartureTime < sourceTrip.DepartureTime
            && TripDelaySupport.ResolveAdjustedArrival(x) > sourceTrip.DepartureTime);
        return new BoatReplanCandidateDto(
            boat.Id,
            boat.Code,
            boat.Name,
            boat.SeatCount,
            earliest,
            canTake,
            false,
            conflicts.Select(x => x.Id).ToArray(),
            canTake ? null : "Tàu có chuyến bị chồng thời gian trước thời điểm bàn giao.");
    }

    private static async Task<Dictionary<Guid, int>> LoadPassengerCountsAsync(
        IApplicationDbContext context,
        IReadOnlyCollection<Guid> tripIds,
        CancellationToken cancellationToken) =>
        await context.Set<BookingPassenger>()
            .AsNoTracking()
            .Where(x => ((x.TripId.HasValue && tripIds.Contains(x.TripId.Value))
                    || (!x.TripId.HasValue && x.Booking.TripId.HasValue && tripIds.Contains(x.Booking.TripId.Value)))
                && x.Booking.BookingStatus == BookingStatus.Confirmed)
            .Select(x => new { TripId = x.TripId ?? x.Booking.TripId })
            .Where(x => x.TripId.HasValue)
            .GroupBy(x => x.TripId!.Value)
            .Select(x => new { TripId = x.Key, Count = x.Count() })
            .ToDictionaryAsync(x => x.TripId, x => x.Count, cancellationToken);

    private static IReadOnlyList<Notification> AddNotifications(
        IApplicationDbContext context,
        IEnumerable<Guid> userIds,
        string title,
        string body,
        string type,
        Guid relatedTripId,
        DateTimeOffset now)
    {
        var notifications = userIds
            .Distinct()
            .Select(userId => new Notification
            {
                UserId = userId,
                Title = title,
                Body = body,
                Type = type,
                RelatedEntityType = NotificationRelatedEntityTypes.Trip,
                RelatedEntityId = relatedTripId,
                CreatedAt = now
            })
            .ToList();
        context.Set<Notification>().AddRange(notifications);
        return notifications;
    }
}
