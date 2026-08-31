using FluentValidation.Results;
using SaigonWaterbus.Application.Boats;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Application.Notifications;
using SaigonWaterbus.Application.Trips;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.Incidents;

public sealed record AssignReplacementBoatCommand(
    Guid IncidentId,
    Guid RescueBoatId,
    Guid? ReplacementBoatId,
    int? DelayMinutes,
    string? Note) : IRequest<IncidentDto>;

public sealed class AssignReplacementBoatCommandValidator : AbstractValidator<AssignReplacementBoatCommand>
{
    public AssignReplacementBoatCommandValidator()
    {
        RuleFor(x => x.IncidentId).NotEmpty();
        RuleFor(x => x.RescueBoatId).NotEmpty();
        RuleFor(x => x.ReplacementBoatId).NotEmpty().When(x => x.ReplacementBoatId.HasValue);
        RuleFor(x => x.DelayMinutes).GreaterThanOrEqualTo(0).When(x => x.DelayMinutes.HasValue);
        RuleFor(x => x.Note).MaximumLength(1000);
    }
}

public sealed class AssignReplacementBoatCommandHandler : IRequestHandler<AssignReplacementBoatCommand, IncidentDto>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;
    private readonly TimeProvider _timeProvider;
    private readonly IIncidentRealtimeNotifier _realtimeNotifier;
    private readonly IIncidentGpsHookNotifier _gpsHookNotifier;
    private readonly INotificationRealtimeNotifier _notificationRealtimeNotifier;

    public AssignReplacementBoatCommandHandler(
        IApplicationDbContext context,
        IUserContext userContext,
        TimeProvider timeProvider,
        IIncidentRealtimeNotifier? realtimeNotifier = null,
        IIncidentGpsHookNotifier? gpsHookNotifier = null,
        INotificationRealtimeNotifier? notificationRealtimeNotifier = null)
    {
        _context = context;
        _userContext = userContext;
        _timeProvider = timeProvider;
        _realtimeNotifier = realtimeNotifier ?? NullIncidentRealtimeNotifier.Instance;
        _gpsHookNotifier = gpsHookNotifier ?? NullIncidentGpsHookNotifier.Instance;
        _notificationRealtimeNotifier = notificationRealtimeNotifier ?? NullNotificationRealtimeNotifier.Instance;
    }

    public async Task<IncidentDto> Handle(
        AssignReplacementBoatCommand request,
        CancellationToken cancellationToken)
    {
        var actor = await IncidentSupport.EnsureCurrentUserCanResolveIncidentAsync(
            _context,
            _userContext,
            cancellationToken);

        var incident = await LoadIncidentQuery()
            .SingleOrDefaultAsync(x => x.Id == request.IncidentId, cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy sự cố.");
        IncidentSupport.EnsureManagerCanAccessIncident(actor, incident);

        if (incident.BoatId == request.RescueBoatId)
        {
            throw new ValidationException([new ValidationFailure(
                nameof(request.RescueBoatId),
                "Tàu cứu hộ không được trùng với tàu gặp sự cố.")]);
        }

        var rescueBoat = await _context.Boats
            .SingleOrDefaultAsync(x => x.Id == request.RescueBoatId, cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy tàu cứu hộ.");
        EnsureRescueBoatReady(rescueBoat);

        var passengerImpact = await IncidentSupport.BuildPassengerImpactPlanAsync(
            _context,
            incident,
            cancellationToken);
        var nextTrips = await IncidentDispatchPlanSupport.LoadNextTripsAsync(
            _context,
            incident,
            asNoTracking: false,
            cancellationToken);

        Boat? replacementBoat = null;
        var passengerReplacementRequired = passengerImpact.AffectedPassengerCount > 0;
        var hasNextTrips = nextTrips.Count > 0;
        var replacementRequested = request.ReplacementBoatId.HasValue;
        if (passengerReplacementRequired && !replacementRequested)
        {
            throw new ValidationException([new ValidationFailure(
                nameof(request.ReplacementBoatId),
                BuildReplacementRequiredMessage(passengerImpact))]);
        }

        if (hasNextTrips && !replacementRequested && request.DelayMinutes.GetValueOrDefault() <= 0)
        {
            throw new ValidationException([new ValidationFailure(
                nameof(request.DelayMinutes),
                $"Tàu gặp sự cố còn {nextTrips.Count} chuyến kế tiếp. Phải chọn tàu thay thế hoặc nhập delayMinutes lớn hơn 0.")]);
        }

        if (!passengerReplacementRequired && !hasNextTrips && replacementRequested && incident.Trip is null)
        {
            throw new ValidationException([new ValidationFailure(
                nameof(request.ReplacementBoatId),
                "Sự cố không có khách bị ảnh hưởng hoặc chuyến kế tiếp nên chỉ chọn tàu cứu hộ.")]);
        }

        if (replacementRequested)
        {
            var replacementBoatId = request.ReplacementBoatId.GetValueOrDefault();
            if (replacementBoatId == incident.BoatId)
            {
                throw new ValidationException([new ValidationFailure(
                    nameof(request.ReplacementBoatId),
                    "Tàu thay thế không được trùng với tàu gặp sự cố.")]);
            }

            if (replacementBoatId == request.RescueBoatId)
            {
                throw new ValidationException([new ValidationFailure(
                    nameof(request.ReplacementBoatId),
                    "Tàu thay thế chở khách không được trùng với tàu cứu hộ.")]);
            }

            replacementBoat = await _context.Boats
                .Include(x => x.Seats)
                .SingleOrDefaultAsync(x => x.Id == replacementBoatId, cancellationToken)
                ?? throw new NotFoundException("Không tìm thấy tàu thay thế.");
            EnsurePassengerReplacementBoatReady(replacementBoat);

            var availableSeatCount = replacementBoat.Seats.Any()
                ? replacementBoat.Seats.Count(x => x.IsActive)
                : replacementBoat.SeatCount;
            if (passengerReplacementRequired
                && availableSeatCount < passengerImpact.AffectedPassengerCount)
            {
                throw new ValidationException([new ValidationFailure(
                    nameof(request.ReplacementBoatId),
                    $"Tàu thay thế không đủ ghế. Cần tối thiểu {passengerImpact.AffectedPassengerCount} ghế cho khách bị ảnh hưởng.")]);
            }

            await IncidentDispatchPlanSupport.EnsureReplacementBoatEligibleAsync(
                _context,
                replacementBoat,
                nextTrips,
                cancellationToken);
        }
        var assignedAt = _timeProvider.GetUtcNow();
        var delayMinutes = request.DelayMinutes.GetValueOrDefault();
        var estimatedResumeAt = ResolveEstimatedResumeAt(
            passengerImpact,
            nextTrips,
            assignedAt,
            delayMinutes);
        incident.RescueBoatId = rescueBoat.Id;
        incident.RescueBoat = rescueBoat;
        incident.RescueDispatchedAt = assignedAt;
        incident.RescueDispatchedByUserId = actor.Id;
        incident.RescueDispatchedByUser = actor;
        incident.ReplacementBoatId = replacementBoat?.Id;
        incident.ReplacementBoat = replacementBoat;
        incident.ReplacementAssignedAt = replacementBoat is null ? null : assignedAt;
        incident.ReplacementAssignedByUserId = replacementBoat is null ? null : actor.Id;
        incident.ReplacementAssignedByUser = replacementBoat is null ? null : actor;
        incident.ReplacementMissionType = passengerImpact.ReplacementMissionType == IncidentReplacementMissionTypes.None
            && hasNextTrips
            && replacementBoat is not null
                ? IncidentReplacementMissionTypes.ScheduledTrips
                : passengerImpact.ReplacementMissionType;
        incident.ReplacementTargetStationId = passengerImpact.TargetStationId;
        incident.ReplacementTargetStopOrder = passengerImpact.TargetStopOrder;
        incident.ReplacementDelayMinutes = delayMinutes;
        incident.ReplacementEstimatedResumeAt = estimatedResumeAt;
        incident.ActiveTicketCountSnapshot = passengerImpact.ActiveTicketCount;
        incident.OnboardPassengerCountSnapshot = passengerImpact.OnboardPassengerCount;
        incident.FuturePassengerCountSnapshot = passengerImpact.FuturePassengerCount;
        incident.ReplacementNote = NormalizeNote(request.Note)
            ?? BuildDefaultReplacementNote(passengerImpact, nextTrips, replacementBoat, delayMinutes);
        incident.MissionStatus = replacementBoat is null
            ? IncidentMissionStatuses.RescueDispatched
            : IncidentMissionStatuses.ReplacementDispatched;

        var createdNotifications = new List<Notification>();
        if (incident.Trip is not null)
        {
            var oldDelayMinutes = incident.Trip.DelayMinutes;
            if (delayMinutes > oldDelayMinutes)
            {
                var delayStartStopOrder = passengerImpact.TargetStopOrder
                    ?? TripDelaySupport.ResolveDelayStartStopOrder(incident.Trip);
                TripDelaySupport.ApplyDelayToTrip(
                    incident.Trip,
                    delayMinutes,
                    incident.ReplacementNote,
                    delayStartStopOrder);
                if (incident.Trip.TripStatus is not TripStatus.Completed and not TripStatus.Cancelled)
                {
                    incident.Trip.TripStatus = TripStatus.Delayed;
                }

                createdNotifications.AddRange(await IncidentDelayNotificationSupport.AddAsync(
                    _context,
                    incident.Trip,
                    delayMinutes - oldDelayMinutes,
                    $"Chuyến {incident.Trip.TripCode} bị trễ {delayMinutes} phút do sự cố tàu.",
                    assignedAt,
                    cancellationToken));
            }

            if (replacementBoat is not null && passengerImpact.AffectedPassengerCount > 0)
            {
                incident.Trip.BoatId = replacementBoat.Id;
                incident.Trip.Boat = replacementBoat;
            }

            incident.Trip.StatusNote = incident.ReplacementNote
                ?? "Đã điều tàu cứu hộ cho sự cố.";
        }

        if (replacementBoat is not null && nextTrips.Count > 0)
        {
            await IncidentDispatchPlanSupport.AssignNextTripsAsync(
                _context,
                nextTrips,
                replacementBoat,
                $"Đã chuyển sang tàu thay thế {replacementBoat.Name} do tàu {incident.Boat.Code} gặp sự cố.",
                cancellationToken);
        }

        if (replacementBoat is not null)
        {
            var transferredTrips = nextTrips.AsEnumerable();
            if (incident.Trip is not null && incident.Trip.BoatId == replacementBoat.Id)
            {
                transferredTrips = transferredTrips.Prepend(incident.Trip);
            }

            await IncidentDispatchPlanSupport.TransferCoveringCrewAssignmentsAsync(
                _context,
                incident.BoatId,
                replacementBoat,
                transferredTrips,
                cancellationToken);
        }

        DateTimeOffset? previousBoatAvailableAt = incident.Trip is null
            ? null
            : TripDelaySupport.ResolveAdjustedArrival(incident.Trip);
        foreach (var nextTrip in nextTrips)
        {
            var oldDelayMinutes = nextTrip.DelayMinutes;
            var cascadedDelayMinutes = previousBoatAvailableAt.HasValue
                ? TripDelaySupport.CalculateCascadedTotalDelayMinutes(
                    nextTrip,
                    previousBoatAvailableAt.Value)
                : Math.Max(oldDelayMinutes, delayMinutes);
            if (cascadedDelayMinutes > oldDelayMinutes)
            {
                TripDelaySupport.ApplyTotalDelayToFutureTrip(
                    nextTrip,
                    cascadedDelayMinutes,
                    incident.ReplacementNote ?? $"Tàu {incident.Boat.Code} gặp sự cố.");
            }

            var addedDelayMinutes = nextTrip.DelayMinutes - oldDelayMinutes;
            createdNotifications.AddRange(await IncidentDelayNotificationSupport.AddAsync(
                _context,
                nextTrip,
                addedDelayMinutes,
                $"Chuyến {nextTrip.TripCode} dự kiến khởi hành trễ thêm {addedDelayMinutes} phút do tàu gặp sự cố.",
                assignedAt,
                cancellationToken));
            previousBoatAvailableAt = TripDelaySupport.ResolveAdjustedArrival(nextTrip);
        }

        await TripDelaySupport.ExtendCoveringBoatAssignmentsAsync(
            _context,
            nextTrips.AsEnumerable().Concat(incident.Trip is null ? [] : [incident.Trip]),
            cancellationToken);

        createdNotifications.AddRange(await NotificationSupport.AddIncidentDispatchedNotificationsAsync(
            _context,
            incident,
            assignedAt,
            cancellationToken));

        await _context.SaveChangesAsync(cancellationToken);
        await NotificationSupport.PublishCreatedAsync(
            _notificationRealtimeNotifier,
            createdNotifications,
            cancellationToken);

        incident = await LoadIncidentQuery().SingleAsync(x => x.Id == request.IncidentId, cancellationToken);
        var activeTicketCount = incident.TripId.HasValue
            ? await IncidentSupport.CountActiveTicketsAsync(_context, incident.TripId.Value, cancellationToken)
            : 0;
        await IncidentSupport.PublishGpsHookAsync(
            _context,
            _gpsHookNotifier,
            incident,
            IncidentSupport.RescueDispatchedEvent,
            cancellationToken);
        await _realtimeNotifier.PublishChangedAsync(
            IncidentSupport.ToRealtimeEvent(incident, IncidentSupport.RescueDispatchedEvent, incident.RescueDispatchedAt),
            cancellationToken);
        return IncidentSupport.ToDto(incident, incident.ActiveTicketCountSnapshot);
    }

    private static void EnsureRescueBoatReady(Boat rescueBoat)
    {
        if (rescueBoat.ServiceType != BoatServiceType.Rescue || rescueBoat.Status != BoatStatus.Active)
        {
            throw new ValidationException([new ValidationFailure(
                nameof(AssignReplacementBoatCommand.RescueBoatId),
                "Tàu cứu hộ phải có serviceType Rescue và đang Active.")]);
        }
    }

    private static void EnsurePassengerReplacementBoatReady(Boat replacementBoat)
    {
        if (replacementBoat.ServiceType != BoatServiceType.Passenger
            || replacementBoat.Status != BoatStatus.Active
            || !BoatSupport.IsReadyForOperation(replacementBoat))
        {
            throw new ValidationException([new ValidationFailure(
                nameof(AssignReplacementBoatCommand.ReplacementBoatId),
                "Tàu thay thế phải là Passenger, Active và đã setup đủ ghế.")]);
        }
    }

    private static string? NormalizeNote(string? note) =>
        string.IsNullOrWhiteSpace(note) ? null : note.Trim();

    private static string BuildReplacementRequiredMessage(
        IncidentPassengerImpactPlan passengerImpact)
    {
        if (passengerImpact.OnboardPassengerCount > 0)
        {
            return $"Có {passengerImpact.OnboardPassengerCount} khách đang ở trên tàu nên phải chọn tàu chở khách thay thế.";
        }

        if (passengerImpact.FuturePassengerCount > 0 && passengerImpact.TargetStationName is not null)
        {
            return $"Có {passengerImpact.FuturePassengerCount} khách chờ ở bến {passengerImpact.TargetStationName} nên phải chọn tàu chở khách thay thế.";
        }

        return $"Chuyến có {passengerImpact.AffectedPassengerCount} khách bị ảnh hưởng nên phải chọn tàu chở khách thay thế.";
    }

    private static string? BuildDefaultReplacementNote(
        IncidentPassengerImpactPlan passengerImpact,
        IReadOnlyList<Trip> nextTrips,
        Boat? replacementBoat,
        int delayMinutes)
    {
        if (replacementBoat is null)
        {
            return nextTrips.Count > 0 && delayMinutes > 0
                ? $"Chưa có tàu thay thế; đã delay {nextTrips.Count} chuyến kế tiếp {delayMinutes} phút."
                : null;
        }

        if (passengerImpact.ReplacementMissionType == IncidentReplacementMissionTypes.TransferAtIncidentLocation)
        {
            return $"Đã điều tàu {replacementBoat.Name} tới vị trí sự cố để chuyển {passengerImpact.OnboardPassengerCount} khách đang trên tàu.";
        }

        if (passengerImpact.ReplacementMissionType == IncidentReplacementMissionTypes.ContinueFromStation
            && passengerImpact.TargetStationName is not null)
        {
            return passengerImpact.FuturePassengerCount > 0
                ? $"Đã điều tàu {replacementBoat.Name} tới bến {passengerImpact.TargetStationName} để đón {passengerImpact.FuturePassengerCount} khách chờ đi tiếp."
                : $"Đã điều tàu {replacementBoat.Name} tới bến {passengerImpact.TargetStationName} để tiếp tục hành trình.";
        }

        return passengerImpact.AffectedPassengerCount > 0
            ? $"Đã điều tàu {replacementBoat.Name} thay thế cho {passengerImpact.AffectedPassengerCount} khách bị ảnh hưởng."
            : nextTrips.Count > 0
                ? $"Đã chuyển {nextTrips.Count} chuyến kế tiếp sang tàu thay thế {replacementBoat.Name}."
                : $"Đã điều tàu {replacementBoat.Name} thay thế để tiếp tục hành trình.";
    }

    private static DateTimeOffset? ResolveEstimatedResumeAt(
        IncidentPassengerImpactPlan passengerImpact,
        IReadOnlyList<Trip> nextTrips,
        DateTimeOffset assignedAt,
        int delayMinutes)
    {
        if (passengerImpact.ReplacementMissionType == IncidentReplacementMissionTypes.None)
        {
            return nextTrips.Count == 0
                ? null
                : IncidentDispatchPlanSupport.ResolveDeparture(nextTrips[0]).AddMinutes(delayMinutes);
        }

        var baseTime = passengerImpact.ReplacementMissionType == IncidentReplacementMissionTypes.ContinueFromStation
            ? passengerImpact.TargetPlannedDepartureAt
                ?? passengerImpact.TargetPlannedArrivalAt
                ?? assignedAt
            : assignedAt;

        return baseTime.AddMinutes(delayMinutes);
    }

    private IQueryable<Incident> LoadIncidentQuery() =>
        _context.Incidents
            .Include(x => x.Boat)
            .Include(x => x.Trip)
                .ThenInclude(x => x!.Route)
                    .ThenInclude(x => x.RouteStops)
            .Include(x => x.Trip)
                .ThenInclude(x => x!.TripStops)
            .Include(x => x.Reporter)
            .Include(x => x.AssignedManager)
            .Include(x => x.AssignedByUser)
            .Include(x => x.RescueBoat)
            .Include(x => x.RescueDispatchedByUser)
            .Include(x => x.ReplacementBoat)
            .Include(x => x.ReplacementAssignedByUser)
            .Include(x => x.ReplacementTargetStation)
            .Include(x => x.Resolver);

}
