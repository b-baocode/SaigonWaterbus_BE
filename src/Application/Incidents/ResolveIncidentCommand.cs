using FluentValidation.Results;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Enums;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.Incidents;

public sealed record ResolveIncidentCommand(
    Guid IncidentId,
    string ResolutionNote,
    BoatStatus? BoatStatus,
    TripStatus? TripStatus) : IRequest<IncidentDto>;

public sealed class ResolveIncidentCommandValidator : AbstractValidator<ResolveIncidentCommand>
{
    public ResolveIncidentCommandValidator()
    {
        RuleFor(x => x.IncidentId).NotEmpty();
        RuleFor(x => x.ResolutionNote).NotEmpty().MaximumLength(1000);
        RuleFor(x => x.BoatStatus).IsInEnum();
        RuleFor(x => x.TripStatus).IsInEnum();
    }
}

public sealed class ResolveIncidentCommandHandler : IRequestHandler<ResolveIncidentCommand, IncidentDto>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;
    private readonly TimeProvider _timeProvider;
    private readonly IIncidentRealtimeNotifier _realtimeNotifier;
    private readonly IIncidentGpsHookNotifier _gpsHookNotifier;

    public ResolveIncidentCommandHandler(
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

    public async Task<IncidentDto> Handle(ResolveIncidentCommand request, CancellationToken cancellationToken)
    {
        var actor = await IncidentSupport.EnsureCurrentUserCanResolveIncidentAsync(
            _context,
            _userContext,
            cancellationToken);

        var incident = await _context.Incidents
            .Include(x => x.Boat)
            .Include(x => x.Trip)
            .Include(x => x.Reporter)
            .Include(x => x.AssignedManager)
            .Include(x => x.AssignedByUser)
            .Include(x => x.RescueBoat)
            .Include(x => x.RescueDispatchedByUser)
            .Include(x => x.ReplacementBoat)
            .Include(x => x.ReplacementAssignedByUser)
            .Include(x => x.Resolver)
            .SingleOrDefaultAsync(x => x.Id == request.IncidentId, cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy sự cố.");

        if (string.Equals(incident.ResolutionStatus, IncidentSupport.ResolvedStatus, StringComparison.OrdinalIgnoreCase))
        {
            throw new ValidationException([new ValidationFailure("incident", "Sự cố này đã được xử lý.")]);
        }

        var resolvedAt = _timeProvider.GetUtcNow();
        incident.ResolutionStatus = IncidentSupport.ResolvedStatus;
        incident.ResolutionNote = request.ResolutionNote.Trim();
        incident.ResolvedAt = resolvedAt;
        incident.ResolvedByUserId = actor.Id;
        incident.Resolver = actor;

        if (request.BoatStatus.HasValue)
        {
            if (request.BoatStatus.Value == BoatStatus.UnderMaintenance
                && incident.Boat.Status != BoatStatus.UnderMaintenance)
            {
                incident.Boat.MaintenanceStartedAt = resolvedAt;
            }

            incident.Boat.Status = request.BoatStatus.Value;
        }

        if (request.TripStatus.HasValue && incident.Trip is not null)
        {
            incident.Trip.TripStatus = request.TripStatus.Value;
            incident.Trip.StatusNote = request.ResolutionNote.Trim();
        }

        await _context.SaveChangesAsync(cancellationToken);
        await IncidentSupport.PublishGpsHookAsync(
            _context,
            _gpsHookNotifier,
            incident,
            IncidentSupport.IncidentResolvedEvent,
            cancellationToken);
        await _realtimeNotifier.PublishChangedAsync(
            IncidentSupport.ToRealtimeEvent(incident, IncidentSupport.IncidentResolvedEvent, incident.ResolvedAt),
            cancellationToken);

        var activeTicketCount = incident.TripId.HasValue
            ? await IncidentSupport.CountActiveTicketsAsync(_context, incident.TripId.Value, cancellationToken)
            : 0;
        return IncidentSupport.ToDto(incident, activeTicketCount);
    }
}
