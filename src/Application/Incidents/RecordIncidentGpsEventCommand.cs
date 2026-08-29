using FluentValidation.Results;
using SaigonWaterbus.Application.Common;
using SaigonWaterbus.Application.Common.Exceptions;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Application.Notifications;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.Incidents;

public sealed record RecordIncidentGpsEventCommand(
    Guid IncidentId,
    string GpsEventId,
    string Event,
    string BoatCode,
    DateTimeOffset OccurredAt,
    decimal? Lat,
    decimal? Lng,
    Guid? StationId,
    string? StationCode,
    string? Note,
    string? PreviousMissionStatus,
    int? EstimatedTowingMinutes) : IRequest<IncidentGpsEventResultDto>;

public sealed record IncidentGpsEventResultDto(
    bool Accepted,
    string GpsEventId,
    string? PreviousMissionStatus,
    string? MissionStatus,
    string OperatingStatus,
    bool CanReplacementContinueTrip,
    bool CanRescueStartTowing,
    string? CurrentOperatingBoatCode);

public sealed class RecordIncidentGpsEventCommandValidator : AbstractValidator<RecordIncidentGpsEventCommand>
{
    public RecordIncidentGpsEventCommandValidator()
    {
        RuleFor(x => x.IncidentId).NotEmpty();
        RuleFor(x => x.GpsEventId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Event)
            .NotEmpty()
            .MaximumLength(50)
            .Must(x => IncidentGpsEventTypes.All.Contains(x))
            .WithMessage("event chỉ nhận RescueArrived, ReplacementArrived, PassengerTransferCompleted, TowingStarted hoặc TowingCompleted.");
        RuleFor(x => x.BoatCode).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Lat).InclusiveBetween(-90m, 90m).When(x => x.Lat.HasValue);
        RuleFor(x => x.Lng).InclusiveBetween(-180m, 180m).When(x => x.Lng.HasValue);
        RuleFor(x => x.StationCode).MaximumLength(50);
        RuleFor(x => x.Note).MaximumLength(1000);
        RuleFor(x => x.PreviousMissionStatus).MaximumLength(50);
        RuleFor(x => x.EstimatedTowingMinutes)
            .GreaterThanOrEqualTo(0)
            .When(x => x.EstimatedTowingMinutes.HasValue);
    }
}

public sealed class RecordIncidentGpsEventCommandHandler
    : IRequestHandler<RecordIncidentGpsEventCommand, IncidentGpsEventResultDto>
{
    private readonly IApplicationDbContext _context;
    private readonly TimeProvider _timeProvider;
    private readonly IIncidentRealtimeNotifier _realtimeNotifier;
    private readonly INotificationRealtimeNotifier _notificationRealtimeNotifier;

    public RecordIncidentGpsEventCommandHandler(
        IApplicationDbContext context,
        TimeProvider timeProvider,
        IIncidentRealtimeNotifier? realtimeNotifier = null,
        INotificationRealtimeNotifier? notificationRealtimeNotifier = null)
    {
        _context = context;
        _timeProvider = timeProvider;
        _realtimeNotifier = realtimeNotifier ?? NullIncidentRealtimeNotifier.Instance;
        _notificationRealtimeNotifier = notificationRealtimeNotifier ?? NullNotificationRealtimeNotifier.Instance;
    }

    public async Task<IncidentGpsEventResultDto> Handle(
        RecordIncidentGpsEventCommand request,
        CancellationToken cancellationToken)
    {
        var normalizedGpsEventId = request.GpsEventId.Trim();
        var existingEvent = await _context.IncidentMissionEvents
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.IncidentId == request.IncidentId && x.GpsEventId == normalizedGpsEventId,
                cancellationToken);

        if (existingEvent is not null)
        {
            EnsureSamePayload(existingEvent, request);
            var incidentForReplay = await LoadIncidentAsync(request.IncidentId, cancellationToken);
            return ToResult(existingEvent, incidentForReplay);
        }

        var incident = await LoadIncidentAsync(request.IncidentId, cancellationToken);
        if (string.Equals(incident.ResolutionStatus, IncidentSupport.ResolvedStatus, StringComparison.Ordinal))
        {
            throw new ConflictException("Sự cố đã được xử lý, không nhận thêm GPS event mới.");
        }

        var previousMissionStatus = ResolveMissionStatus(incident);
        var oldTripStatus = incident.Trip?.TripStatus;
        var occurredAt = request.OccurredAt.ToUniversalTime();
        var eventType = request.Event.Trim();
        var boatCode = request.BoatCode.Trim();
        var gpsEvent = new IncidentMissionEvent
        {
            IncidentId = incident.Id,
            GpsEventId = normalizedGpsEventId,
            Event = eventType,
            BoatCode = boatCode,
            OccurredAt = occurredAt,
            Latitude = request.Lat,
            Longitude = request.Lng,
            StationId = request.StationId,
            StationCode = NormalizeOptional(request.StationCode),
            Note = NormalizeOptional(request.Note),
            ReportedPreviousMissionStatus = NormalizeOptional(request.PreviousMissionStatus),
            EstimatedTowingMinutes = request.EstimatedTowingMinutes,
            PreviousMissionStatus = previousMissionStatus,
            CreatedAt = _timeProvider.GetUtcNow()
        };

        ApplyEvent(incident, gpsEvent, request);
        gpsEvent.MissionStatus = incident.MissionStatus;

        _context.IncidentMissionEvents.Add(gpsEvent);
        var createdNotifications = gpsEvent.Event == IncidentGpsEventTypes.TowingCompleted
            ? await NotificationSupport.AddIncidentResolvedNotificationsAsync(
                _context,
                incident,
                gpsEvent.OccurredAt,
                cancellationToken)
            : await NotificationSupport.AddIncidentProgressNotificationsAsync(
                _context,
                incident,
                gpsEvent.Event,
                gpsEvent.OccurredAt,
                cancellationToken);
        if (incident.Trip is not null && oldTripStatus.HasValue)
        {
            createdNotifications = createdNotifications
                .Concat(await StaffTripNotificationSupport.AddTripStatusChangedNotificationsAsync(
                    _context,
                    incident.Trip,
                    oldTripStatus.Value,
                    gpsEvent.OccurredAt,
                    cancellationToken))
                .Concat(await StaffTripNotificationSupport.AddManagementTripStatusNotificationsAsync(
                    _context,
                    incident.Trip,
                    oldTripStatus.Value,
                    gpsEvent.OccurredAt,
                    cancellationToken))
                .ToList();
        }
        if (gpsEvent.Event == IncidentGpsEventTypes.TowingCompleted)
        {
            await IncidentSupport.ClearBoatLiveTripAsync(
                _context,
                incident.BoatId,
                gpsEvent.OccurredAt,
                IncidentSupport.MaintenanceLocationStatus,
                cancellationToken);
        }

        await _context.SaveChangesAsync(cancellationToken);
        await NotificationSupport.PublishCreatedAsync(
            _notificationRealtimeNotifier,
            createdNotifications,
            cancellationToken);
        await _realtimeNotifier.PublishChangedAsync(
            IncidentSupport.ToRealtimeEvent(incident, gpsEvent.Event, gpsEvent.OccurredAt),
            cancellationToken);

        return ToResult(gpsEvent, incident);
    }

    private async Task<Incident> LoadIncidentAsync(Guid incidentId, CancellationToken cancellationToken) =>
        await _context.Incidents
            .Include(x => x.Boat)
            .Include(x => x.Trip)
                .ThenInclude(x => x!.Boat)
            .Include(x => x.RescueBoat)
            .Include(x => x.ReplacementBoat)
            .Include(x => x.ReplacementTargetStation)
            .SingleOrDefaultAsync(x => x.Id == incidentId, cancellationToken)
        ?? throw new NotFoundException("Không tìm thấy sự cố.");

    private static void ApplyEvent(
        Incident incident,
        IncidentMissionEvent gpsEvent,
        RecordIncidentGpsEventCommand request)
    {
        switch (gpsEvent.Event)
        {
            case IncidentGpsEventTypes.RescueArrived:
                EnsureRescueBoatMatches(incident, gpsEvent.BoatCode);
                EnsureNotRecorded(incident.RescueArrivedAt, IncidentGpsEventTypes.RescueArrived);
                incident.RescueArrivedAt = gpsEvent.OccurredAt;
                incident.MissionStatus = IncidentMissionStatuses.RescueArrived;
                return;

            case IncidentGpsEventTypes.ReplacementArrived:
                EnsureReplacementBoatMatches(incident, gpsEvent.BoatCode);
                EnsureNotRecorded(incident.ReplacementArrivedAt, IncidentGpsEventTypes.ReplacementArrived);
                EnsureReplacementArrivalTargetMatches(incident, request);
                incident.ReplacementArrivedAt = gpsEvent.OccurredAt;
                incident.MissionStatus = IncidentMissionStatuses.ReplacementArrived;
                if (incident.OnboardPassengerCountSnapshot == 0
                    && incident.ReplacementMissionType != IncidentReplacementMissionTypes.ScheduledTrips)
                {
                    SwitchTripToReplacementBoat(incident);
                }
                return;

            case IncidentGpsEventTypes.PassengerTransferCompleted:
                EnsureAssignedMissionBoatMatches(incident, gpsEvent.BoatCode);
                EnsureNotRecorded(
                    incident.PassengerTransferCompletedAt,
                    IncidentGpsEventTypes.PassengerTransferCompleted);
                if (incident.OnboardPassengerCountSnapshot <= 0)
                {
                    throw new ConflictException("Không có khách onboard nên không cần PassengerTransferCompleted.");
                }

                if (!incident.ReplacementArrivedAt.HasValue)
                {
                    throw new ConflictException("Chưa ghi nhận ReplacementArrived nên chưa thể chuyển khách.");
                }

                incident.PassengerTransferCompletedAt = gpsEvent.OccurredAt;
                incident.MissionStatus = IncidentMissionStatuses.PassengerTransferCompleted;
                SwitchTripToReplacementBoat(incident);
                return;

            case IncidentGpsEventTypes.TowingStarted:
                EnsureRescueBoatMatches(incident, gpsEvent.BoatCode);
                EnsureNotRecorded(incident.TowingStartedAt, IncidentGpsEventTypes.TowingStarted);
                if (!incident.RescueArrivedAt.HasValue)
                {
                    throw new ConflictException("Chưa ghi nhận RescueArrived nên chưa thể bắt đầu kéo tàu.");
                }

                if (incident.OnboardPassengerCountSnapshot > 0
                    && !incident.PassengerTransferCompletedAt.HasValue)
                {
                    throw new ConflictException("Còn khách onboard nên phải PassengerTransferCompleted trước khi kéo tàu lỗi.");
                }

                incident.TowingStartedAt = gpsEvent.OccurredAt;
                incident.EstimatedTowingMinutes = request.EstimatedTowingMinutes ?? incident.EstimatedTowingMinutes;
                incident.MissionStatus = IncidentMissionStatuses.TowingStarted;
                return;

            case IncidentGpsEventTypes.TowingCompleted:
                EnsureRescueBoatMatches(incident, gpsEvent.BoatCode);
                EnsureNotRecorded(incident.TowingCompletedAt, IncidentGpsEventTypes.TowingCompleted);
                if (!incident.TowingStartedAt.HasValue)
                {
                    throw new ConflictException("Chưa ghi nhận TowingStarted nên chưa thể hoàn tất kéo tàu.");
                }

                incident.TowingCompletedAt = gpsEvent.OccurredAt;
                incident.MissionStatus = IncidentMissionStatuses.TowingCompleted;
                incident.ResolutionStatus = IncidentSupport.ResolvedStatus;
                incident.ResolvedAt = gpsEvent.OccurredAt;
                incident.ResolutionNote = NormalizeOptional(request.Note) ?? "Tàu lỗi đã được kéo về bến/bảo trì.";
                incident.Boat.Status = BoatStatus.UnderMaintenance;
                incident.Boat.MaintenanceStartedAt = gpsEvent.OccurredAt;
                if (incident.RescueBoat is not null)
                {
                    incident.RescueBoat.Status = BoatStatus.Active;
                }
                IncidentSupport.EnsureTripIsNotRunningOnMaintainedBoat(incident);
                return;
        }
    }

    private static void SwitchTripToReplacementBoat(Incident incident)
    {
        if (incident.Trip is null || incident.ReplacementBoat is null)
        {
            return;
        }

        if (incident.Trip.TripStatus is TripStatus.Completed or TripStatus.Cancelled)
        {
            return;
        }

        incident.Trip.BoatId = incident.ReplacementBoat.Id;
        incident.Trip.Boat = incident.ReplacementBoat;
        incident.Trip.TripStatus = TripStatus.InProgress;
        incident.Trip.StatusNote = $"Chuyến đã chuyển sang tàu thay thế {incident.ReplacementBoat.Name}.";
    }

    private static void EnsureRescueBoatMatches(Incident incident, string boatCode)
    {
        if (incident.RescueBoat is null)
        {
            throw new ConflictException("Sự cố chưa được điều tàu cứu hộ.");
        }

        if (!string.Equals(incident.RescueBoat.Code, boatCode, StringComparison.OrdinalIgnoreCase))
        {
            throw new ConflictException("boatCode không khớp với tàu cứu hộ của sự cố.");
        }
    }

    private static void EnsureReplacementBoatMatches(Incident incident, string boatCode)
    {
        if (incident.ReplacementBoat is null)
        {
            throw new ConflictException("Sự cố chưa được điều tàu thay thế.");
        }

        if (!string.Equals(incident.ReplacementBoat.Code, boatCode, StringComparison.OrdinalIgnoreCase))
        {
            throw new ConflictException("boatCode không khớp với tàu thay thế của sự cố.");
        }
    }

    private static void EnsureAssignedMissionBoatMatches(Incident incident, string boatCode)
    {
        if (incident.ReplacementBoat is not null
            && string.Equals(incident.ReplacementBoat.Code, boatCode, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (incident.RescueBoat is not null
            && string.Equals(incident.RescueBoat.Code, boatCode, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        throw new ConflictException("boatCode không khớp với tàu cứu hộ hoặc tàu thay thế của sự cố.");
    }

    private static void EnsureReplacementArrivalTargetMatches(
        Incident incident,
        RecordIncidentGpsEventCommand request)
    {
        if (incident.ReplacementMissionType == IncidentReplacementMissionTypes.ContinueFromStation)
        {
            if (incident.ReplacementTargetStation is null)
            {
                throw new ConflictException("Sự cố chưa có replacementTargetStation để tàu thay thế tới.");
            }

            var stationMatches = request.StationId == incident.ReplacementTargetStationId
                || (!string.IsNullOrWhiteSpace(request.StationCode)
                    && string.Equals(
                        request.StationCode.Trim(),
                        incident.ReplacementTargetStation.StationCode,
                        StringComparison.OrdinalIgnoreCase));

            if (!stationMatches)
            {
                throw new ConflictException("ReplacementArrived phải gửi đúng replacementTargetStation.");
            }
        }
    }

    private static void EnsureNotRecorded(DateTimeOffset? recordedAt, string eventType)
    {
        if (recordedAt.HasValue)
        {
            throw new ConflictException($"{eventType} đã được ghi nhận trước đó.");
        }
    }

    private static void EnsureSamePayload(
        IncidentMissionEvent existingEvent,
        RecordIncidentGpsEventCommand request)
    {
        if (!string.Equals(existingEvent.Event, request.Event.Trim(), StringComparison.Ordinal)
            || !string.Equals(existingEvent.BoatCode, request.BoatCode.Trim(), StringComparison.OrdinalIgnoreCase)
            || existingEvent.OccurredAt != request.OccurredAt.ToUniversalTime()
            || existingEvent.Latitude != request.Lat
            || existingEvent.Longitude != request.Lng
            || existingEvent.StationId != request.StationId
            || !string.Equals(existingEvent.StationCode, NormalizeOptional(request.StationCode), StringComparison.OrdinalIgnoreCase)
            || !string.Equals(existingEvent.Note, NormalizeOptional(request.Note), StringComparison.Ordinal)
            || !string.Equals(existingEvent.ReportedPreviousMissionStatus, NormalizeOptional(request.PreviousMissionStatus), StringComparison.Ordinal)
            || existingEvent.EstimatedTowingMinutes != request.EstimatedTowingMinutes)
        {
            throw new ConflictException("gpsEventId đã tồn tại nhưng payload khác request trước đó.");
        }
    }

    private static IncidentGpsEventResultDto ToResult(
        IncidentMissionEvent gpsEvent,
        Incident incident) =>
        new(
            Accepted: true,
            gpsEvent.GpsEventId,
            OperatingStatusSupport.ToPublicMissionStatus(gpsEvent.PreviousMissionStatus),
            OperatingStatusSupport.ToPublicMissionStatus(incident),
            OperatingStatusSupport.ForIncident(incident),
            CanReplacementContinueTrip: CanReplacementContinueTrip(incident),
            CanRescueStartTowing: CanRescueStartTowing(incident),
            CurrentOperatingBoatCode: incident.Trip?.Boat?.Code
                ?? (incident.Trip?.BoatId == incident.ReplacementBoatId ? incident.ReplacementBoat?.Code : null)
                ?? incident.Boat.Code);

    private static bool CanReplacementContinueTrip(Incident incident) =>
        incident.ReplacementBoatId.HasValue
        && incident.Trip?.BoatId == incident.ReplacementBoatId
        && incident.Trip.TripStatus is not TripStatus.Completed and not TripStatus.Cancelled;

    private static bool CanRescueStartTowing(Incident incident) =>
        incident.RescueArrivedAt.HasValue
        && (incident.OnboardPassengerCountSnapshot <= 0 || incident.PassengerTransferCompletedAt.HasValue)
        && !incident.TowingStartedAt.HasValue;

    private static string ResolveMissionStatus(Incident incident) =>
        string.IsNullOrWhiteSpace(incident.MissionStatus)
            ? IncidentMissionStatuses.IncidentCreated
            : incident.MissionStatus;

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
