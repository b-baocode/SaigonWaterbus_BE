using FluentValidation.Results;
using SaigonWaterbus.Application.Boats;
using SaigonWaterbus.Application.Common.Interfaces;
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

    public AssignReplacementBoatCommandHandler(
        IApplicationDbContext context,
        IUserContext userContext,
        TimeProvider timeProvider,
        IIncidentRealtimeNotifier? realtimeNotifier = null,
        IIncidentGpsHookNotifier? gpsHookNotifier = null)
    {
        _context = context;
        _userContext = userContext;
        _timeProvider = timeProvider;
        _realtimeNotifier = realtimeNotifier ?? NullIncidentRealtimeNotifier.Instance;
        _gpsHookNotifier = gpsHookNotifier ?? NullIncidentGpsHookNotifier.Instance;
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

        Boat? replacementBoat = null;
        if (passengerImpact.AffectedPassengerCount > 0)
        {
            if (!request.ReplacementBoatId.HasValue)
            {
                throw new ValidationException([new ValidationFailure(
                    nameof(request.ReplacementBoatId),
                    BuildReplacementRequiredMessage(passengerImpact))]);
            }

            if (request.ReplacementBoatId.Value == incident.BoatId)
            {
                throw new ValidationException([new ValidationFailure(
                    nameof(request.ReplacementBoatId),
                    "Tàu thay thế không được trùng với tàu gặp sự cố.")]);
            }

            if (request.ReplacementBoatId.Value == request.RescueBoatId)
            {
                throw new ValidationException([new ValidationFailure(
                    nameof(request.ReplacementBoatId),
                    "Tàu thay thế chở khách không được trùng với tàu cứu hộ.")]);
            }

            replacementBoat = await _context.Boats
                .Include(x => x.Seats)
                .SingleOrDefaultAsync(x => x.Id == request.ReplacementBoatId.Value, cancellationToken)
                ?? throw new NotFoundException("Không tìm thấy tàu thay thế.");
            EnsurePassengerReplacementBoatReady(replacementBoat);

            if (replacementBoat.SeatCount < passengerImpact.AffectedPassengerCount)
            {
                throw new ValidationException([new ValidationFailure(
                    nameof(request.ReplacementBoatId),
                    $"Tàu thay thế không đủ ghế. Cần tối thiểu {passengerImpact.AffectedPassengerCount} ghế cho khách bị ảnh hưởng.")]);
            }
        }
        else if (request.ReplacementBoatId.HasValue)
        {
            throw new ValidationException([new ValidationFailure(
                nameof(request.ReplacementBoatId),
                "Sự cố không có khách cần chuyển nên chỉ chọn tàu cứu hộ.")]);
        }

        var assignedAt = _timeProvider.GetUtcNow();
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
        incident.ReplacementMissionType = passengerImpact.ReplacementMissionType;
        incident.ReplacementTargetStationId = passengerImpact.TargetStationId;
        incident.ReplacementTargetStopOrder = passengerImpact.TargetStopOrder;
        incident.ActiveTicketCountSnapshot = passengerImpact.ActiveTicketCount;
        incident.OnboardPassengerCountSnapshot = passengerImpact.OnboardPassengerCount;
        incident.FuturePassengerCountSnapshot = passengerImpact.FuturePassengerCount;
        incident.ReplacementNote = NormalizeNote(request.Note)
            ?? BuildDefaultReplacementNote(passengerImpact, replacementBoat);

        if (incident.Trip is not null && replacementBoat is not null)
        {
            incident.Trip.BoatId = replacementBoat.Id;
            incident.Trip.Boat = replacementBoat;
            if (incident.Trip.TripStatus is not TripStatus.Completed and not TripStatus.Cancelled)
            {
                if (request.DelayMinutes.GetValueOrDefault() > 0)
                {
                    incident.Trip.TripStatus = TripStatus.Delayed;
                }
                else if (incident.Trip.TripStatus == TripStatus.Delayed)
                {
                    incident.Trip.TripStatus = TripStatus.Scheduled;
                }
            }

            incident.Trip.StatusNote = incident.ReplacementNote
                ?? $"Đã điều tàu {replacementBoat.Name} thay thế.";
        }

        await _context.SaveChangesAsync(cancellationToken);

        incident = await LoadIncidentQuery().SingleAsync(x => x.Id == request.IncidentId, cancellationToken);
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

    private static string BuildReplacementRequiredMessage(IncidentSupport.IncidentPassengerImpactPlan passengerImpact)
    {
        if (passengerImpact.OnboardPassengerCount > 0)
        {
            return $"Có {passengerImpact.OnboardPassengerCount} khách đang ở trên tàu nên phải chọn tàu chở khách thay thế.";
        }

        if (passengerImpact.TargetStationName is not null)
        {
            return $"Có {passengerImpact.FuturePassengerCount} khách chờ ở bến {passengerImpact.TargetStationName} nên phải chọn tàu chở khách thay thế.";
        }

        return $"Chuyến có {passengerImpact.AffectedPassengerCount} khách bị ảnh hưởng nên phải chọn tàu chở khách thay thế.";
    }

    private static string? BuildDefaultReplacementNote(
        IncidentSupport.IncidentPassengerImpactPlan passengerImpact,
        Boat? replacementBoat)
    {
        if (replacementBoat is null)
        {
            return null;
        }

        if (passengerImpact.ReplacementMissionType == IncidentReplacementMissionTypes.TransferAtIncidentLocation)
        {
            return $"Đã điều tàu {replacementBoat.Name} tới vị trí sự cố để chuyển {passengerImpact.OnboardPassengerCount} khách đang trên tàu.";
        }

        if (passengerImpact.ReplacementMissionType == IncidentReplacementMissionTypes.ContinueFromStation
            && passengerImpact.TargetStationName is not null)
        {
            return $"Đã điều tàu {replacementBoat.Name} tới bến {passengerImpact.TargetStationName} để đón {passengerImpact.FuturePassengerCount} khách chờ đi tiếp.";
        }

        return $"Đã điều tàu {replacementBoat.Name} thay thế cho {passengerImpact.AffectedPassengerCount} khách bị ảnh hưởng.";
    }

    private IQueryable<Incident> LoadIncidentQuery() =>
        _context.Incidents
            .Include(x => x.Boat)
            .Include(x => x.Trip)
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
