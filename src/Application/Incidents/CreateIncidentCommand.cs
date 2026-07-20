using FluentValidation.Results;
using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.Incidents;

public sealed record CreateIncidentCommand(
    Guid BoatId,
    Guid? TripId,
    string IncidentType,
    string Description,
    string? Severity,
    DateTimeOffset? OccurredAt) : IRequest<IncidentDto>;

public sealed class CreateIncidentCommandValidator : AbstractValidator<CreateIncidentCommand>
{
    public CreateIncidentCommandValidator()
    {
        RuleFor(x => x.BoatId).NotEmpty();
        RuleFor(x => x.IncidentType).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Description).NotEmpty().MaximumLength(1000);
        RuleFor(x => x.Severity).MaximumLength(30);
    }
}

public sealed class CreateIncidentCommandHandler : IRequestHandler<CreateIncidentCommand, IncidentDto>
{
    private static readonly TimeSpan LatestTripInferenceWindow = TimeSpan.FromMinutes(15);

    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;
    private readonly TimeProvider _timeProvider;
    private readonly IIncidentRealtimeNotifier _realtimeNotifier;
    private readonly IIncidentGpsHookNotifier _gpsHookNotifier;

    public CreateIncidentCommandHandler(
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

    public async Task<IncidentDto> Handle(CreateIncidentCommand request, CancellationToken cancellationToken)
    {
        var actor = await IncidentSupport.EnsureCurrentUserCanReportIncidentAsync(
            _context,
            _userContext,
            cancellationToken);

        var boat = await _context.Boats
            .SingleOrDefaultAsync(x => x.Id == request.BoatId, cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy tàu.");

        var now = _timeProvider.GetUtcNow();
        Trip? trip = null;
        if (request.TripId.HasValue)
        {
            trip = await _context.Set<Trip>()
                .SingleOrDefaultAsync(x => x.Id == request.TripId.Value, cancellationToken)
                ?? throw new NotFoundException("Không tìm thấy chuyến tàu.");

            if (trip.BoatId != boat.Id)
            {
                throw new ValidationException([new ValidationFailure(
                    nameof(request.TripId),
                    "Chuyến tàu không thuộc tàu được báo sự cố.")]);
            }
        }
        else
        {
            trip = await ResolveLatestRunningTripAsync(
                _context,
                boat.Id,
                now,
                cancellationToken);
        }

        var severity = NormalizeOptional(request.Severity);
        var incident = new Incident
        {
            BoatId = boat.Id,
            Boat = boat,
            TripId = trip?.Id,
            Trip = trip,
            ReportedBy = actor.Id,
            Reporter = actor,
            IncidentType = request.IncidentType.Trim(),
            Description = request.Description.Trim(),
            Severity = severity,
            OccurredAt = request.OccurredAt?.ToUniversalTime() ?? now,
            ResolutionStatus = IncidentSupport.OpenStatus
        };

        boat.Status = BoatStatus.Incident;
        if (trip is not null && trip.TripStatus is not TripStatus.Completed and not TripStatus.Cancelled)
        {
            trip.TripStatus = TripStatus.Delayed;
            trip.StatusNote = $"Incident {incident.IncidentType}: {incident.Description}";
        }

        _context.Incidents.Add(incident);
        await _context.SaveChangesAsync(cancellationToken);
        await IncidentSupport.PublishGpsHookAsync(
            _context,
            _gpsHookNotifier,
            incident,
            IncidentSupport.IncidentCreatedEvent,
            cancellationToken);
        await _realtimeNotifier.PublishChangedAsync(
            IncidentSupport.ToRealtimeEvent(incident, IncidentSupport.IncidentCreatedEvent, now),
            cancellationToken);

        var activeTicketCount = trip is null
            ? 0
            : await IncidentSupport.CountActiveTicketsAsync(_context, trip.Id, cancellationToken);
        return IncidentSupport.ToDto(incident, activeTicketCount);
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static async Task<Trip?> ResolveLatestRunningTripAsync(
        IApplicationDbContext context,
        Guid boatId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var latestLocation = await context.BoatLatestLocations
            .AsNoTracking()
            .Where(x => x.BoatId == boatId)
            .Select(x => new { x.TripId, x.ReceivedAt })
            .SingleOrDefaultAsync(cancellationToken);

        if (latestLocation is null
            || !latestLocation.TripId.HasValue
            || now - latestLocation.ReceivedAt > LatestTripInferenceWindow)
        {
            return null;
        }

        return await context.Set<Trip>()
            .SingleOrDefaultAsync(x => x.Id == latestLocation.TripId.Value
                && x.BoatId == boatId
                && x.TripStatus != TripStatus.Completed
                && x.TripStatus != TripStatus.Cancelled,
                cancellationToken);
    }

}
