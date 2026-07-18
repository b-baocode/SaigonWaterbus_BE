using FluentValidation.Results;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Enums;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.Incidents;

public sealed record CompleteRescueMissionCommand(
    Guid IncidentId,
    string BoatCode,
    string RescueBoatCode,
    DateTimeOffset? CompletedAt,
    string? Note) : IRequest<IncidentDto>;

public sealed class CompleteRescueMissionCommandValidator : AbstractValidator<CompleteRescueMissionCommand>
{
    public CompleteRescueMissionCommandValidator()
    {
        RuleFor(x => x.IncidentId).NotEmpty();
        RuleFor(x => x.BoatCode).NotEmpty().MaximumLength(50);
        RuleFor(x => x.RescueBoatCode).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Note).MaximumLength(1000);
    }
}

public sealed class CompleteRescueMissionCommandHandler : IRequestHandler<CompleteRescueMissionCommand, IncidentDto>
{
    private const string DefaultResolutionNote = "Tàu đã được kéo về bến và chuyển sang bảo trì.";

    private readonly IApplicationDbContext _context;
    private readonly TimeProvider _timeProvider;
    private readonly IIncidentRealtimeNotifier _realtimeNotifier;
    private readonly IIncidentGpsHookNotifier _gpsHookNotifier;

    public CompleteRescueMissionCommandHandler(
        IApplicationDbContext context,
        TimeProvider timeProvider,
        IIncidentRealtimeNotifier? realtimeNotifier = null,
        IIncidentGpsHookNotifier? gpsHookNotifier = null)
    {
        _context = context;
        _timeProvider = timeProvider;
        _realtimeNotifier = realtimeNotifier ?? NullIncidentRealtimeNotifier.Instance;
        _gpsHookNotifier = gpsHookNotifier ?? NullIncidentGpsHookNotifier.Instance;
    }

    public async Task<IncidentDto> Handle(
        CompleteRescueMissionCommand request,
        CancellationToken cancellationToken)
    {
        var incident = await LoadIncidentQuery()
            .SingleOrDefaultAsync(x => x.Id == request.IncidentId, cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy sự cố.");

        EnsureCodesMatch(incident, request);

        if (string.Equals(incident.ResolutionStatus, IncidentSupport.ResolvedStatus, StringComparison.OrdinalIgnoreCase))
        {
            return IncidentSupport.ToDto(
                incident,
                await CountActiveTicketsAsync(incident, cancellationToken));
        }

        var completedAt = request.CompletedAt?.ToUniversalTime() ?? _timeProvider.GetUtcNow();
        incident.ResolutionStatus = IncidentSupport.ResolvedStatus;
        incident.ResolutionNote = NormalizeNote(request.Note) ?? DefaultResolutionNote;
        incident.ResolvedAt = completedAt;
        incident.ResolvedByUserId = null;
        incident.Resolver = null;

        if (incident.Boat.Status != BoatStatus.UnderMaintenance)
        {
            incident.Boat.MaintenanceStartedAt = completedAt;
        }
        else if (!incident.Boat.MaintenanceStartedAt.HasValue)
        {
            incident.Boat.MaintenanceStartedAt = completedAt;
        }

        incident.Boat.Status = BoatStatus.UnderMaintenance;
        if (incident.RescueBoat is not null)
        {
            incident.RescueBoat.Status = BoatStatus.Active;
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

        return IncidentSupport.ToDto(
            incident,
            await CountActiveTicketsAsync(incident, cancellationToken));
    }

    private static void EnsureCodesMatch(
        Domain.Entities.Incident incident,
        CompleteRescueMissionCommand request)
    {
        if (!string.Equals(incident.Boat.Code, request.BoatCode.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            throw new ValidationException([new ValidationFailure(
                nameof(request.BoatCode),
                "boatCode không khớp với tàu gặp sự cố.")]);
        }

        if (incident.RescueBoat is null)
        {
            throw new ValidationException([new ValidationFailure(
                nameof(request.RescueBoatCode),
                "Sự cố chưa được điều tàu cứu hộ.")]);
        }

        if (!string.Equals(incident.RescueBoat.Code, request.RescueBoatCode.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            throw new ValidationException([new ValidationFailure(
                nameof(request.RescueBoatCode),
                "rescueBoatCode không khớp với tàu cứu hộ đã điều.")]);
        }
    }

    private async Task<int> CountActiveTicketsAsync(
        Domain.Entities.Incident incident,
        CancellationToken cancellationToken) =>
        incident.TripId.HasValue
            ? await IncidentSupport.CountActiveTicketsAsync(_context, incident.TripId.Value, cancellationToken)
            : 0;

    private static string? NormalizeNote(string? note) =>
        string.IsNullOrWhiteSpace(note) ? null : note.Trim();

    private IQueryable<Domain.Entities.Incident> LoadIncidentQuery() =>
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
            .Include(x => x.Resolver);
}
