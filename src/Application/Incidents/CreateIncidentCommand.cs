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
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;
    private readonly TimeProvider _timeProvider;

    public CreateIncidentCommandHandler(
        IApplicationDbContext context,
        IUserContext userContext,
        TimeProvider timeProvider)
    {
        _context = context;
        _userContext = userContext;
        _timeProvider = timeProvider;
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

        var now = _timeProvider.GetUtcNow();
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

        boat.Status = BoatStatus.Maintenance;
        if (trip is not null && trip.TripStatus is not TripStatus.Completed and not TripStatus.Cancelled)
        {
            trip.TripStatus = IsCritical(severity) ? TripStatus.Cancelled : TripStatus.Delayed;
            trip.StatusNote = $"Incident {incident.IncidentType}: {incident.Description}";
        }

        _context.Incidents.Add(incident);
        await _context.SaveChangesAsync(cancellationToken);

        return IncidentSupport.ToDto(incident);
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool IsCritical(string? severity) =>
        string.Equals(severity, IncidentSupport.CriticalSeverity, StringComparison.OrdinalIgnoreCase)
        || string.Equals(severity, IncidentSupport.HighSeverity, StringComparison.OrdinalIgnoreCase);
}
