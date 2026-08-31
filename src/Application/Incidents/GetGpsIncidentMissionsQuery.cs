using SaigonWaterbus.Application.Common.Interfaces;

namespace SaigonWaterbus.Application.Incidents;

public sealed record GetGpsIncidentMissionsQuery(
    Guid? IncidentId,
    string? BoatCode) : IRequest<GpsIncidentMissionListDto>;

public sealed record GpsIncidentMissionListDto(
    DateTimeOffset ServerTime,
    IReadOnlyList<GpsIncidentMissionDto> Missions);

public sealed record GpsIncidentBoatDto(
    Guid BoatId,
    string BoatCode,
    string BoatName,
    string Role);

public sealed record GpsIncidentMissionDto(
    Guid IncidentId,
    string ResolutionStatus,
    string MissionStatus,
    Guid? TripId,
    string? TripCode,
    GpsIncidentBoatDto IncidentBoat,
    GpsIncidentBoatDto? RescueBoat,
    GpsIncidentBoatDto? ReplacementBoat,
    string? RequestedBoatRole,
    string ReplacementMissionType,
    Guid? ReplacementTargetStationId,
    string? ReplacementTargetStationCode,
    string? ReplacementTargetStationName,
    int? ReplacementTargetStopOrder,
    decimal? ReplacementTargetLat,
    decimal? ReplacementTargetLng,
    int ReplacementDelayMinutes,
    DateTimeOffset? ReplacementEstimatedResumeAt,
    int OnboardPassengerCount,
    int FuturePassengerCount,
    IReadOnlyList<string> RescueNextEvents,
    IReadOnlyList<string> ReplacementNextEvents,
    bool CanReplacementContinueTrip,
    bool CanRescueStartTowing,
    string CurrentOperatingBoatCode);

public sealed class GetGpsIncidentMissionsQueryValidator : AbstractValidator<GetGpsIncidentMissionsQuery>
{
    public GetGpsIncidentMissionsQueryValidator()
    {
        RuleFor(x => x.IncidentId).NotEmpty().When(x => x.IncidentId.HasValue);
        RuleFor(x => x.BoatCode).MaximumLength(50);
    }
}

public sealed class GetGpsIncidentMissionsQueryHandler
    : IRequestHandler<GetGpsIncidentMissionsQuery, GpsIncidentMissionListDto>
{
    private readonly IApplicationDbContext _context;
    private readonly TimeProvider _timeProvider;

    public GetGpsIncidentMissionsQueryHandler(
        IApplicationDbContext context,
        TimeProvider timeProvider)
    {
        _context = context;
        _timeProvider = timeProvider;
    }

    public async Task<GpsIncidentMissionListDto> Handle(
        GetGpsIncidentMissionsQuery request,
        CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();
        var normalizedBoatCode = string.IsNullOrWhiteSpace(request.BoatCode)
            ? null
            : request.BoatCode.Trim().ToUpperInvariant();
        var query = _context.Incidents
            .AsNoTracking()
            .Include(x => x.Boat)
            .Include(x => x.Trip)
                .ThenInclude(x => x!.TripStops)
            .Include(x => x.RescueBoat)
            .Include(x => x.ReplacementBoat)
            .Include(x => x.ReplacementTargetStation)
            .Where(x => x.ResolutionStatus == IncidentSupport.OpenStatus
                && (x.RescueBoatId.HasValue || x.ReplacementBoatId.HasValue));

        if (request.IncidentId.HasValue)
        {
            query = query.Where(x => x.Id == request.IncidentId.Value);
        }

        if (normalizedBoatCode is not null)
        {
            query = query.Where(x => x.Boat.Code.ToUpper() == normalizedBoatCode
                || (x.RescueBoat != null && x.RescueBoat.Code.ToUpper() == normalizedBoatCode)
                || (x.ReplacementBoat != null && x.ReplacementBoat.Code.ToUpper() == normalizedBoatCode));
        }

        var incidents = await query
            .OrderByDescending(x => x.RescueDispatchedAt ?? x.OccurredAt)
            .ThenBy(x => x.Id)
            .ToListAsync(cancellationToken);

        return new GpsIncidentMissionListDto(
            now,
            incidents.Select(incident => ToDto(incident, request.BoatCode, now)).ToArray());
    }

    private static GpsIncidentMissionDto ToDto(
        Domain.Entities.Incident incident,
        string? requestedBoatCode,
        DateTimeOffset now)
    {
        var canReplacementContinueTrip = IncidentGpsMissionSupport.CanReplacementContinueTrip(
            incident,
            now);
        return new GpsIncidentMissionDto(
            incident.Id,
            incident.ResolutionStatus,
            incident.MissionStatus,
            incident.TripId,
            incident.Trip?.TripCode,
            ToBoatDto(incident.Boat, IncidentGpsBoatRoles.Incident),
            incident.RescueBoat is null
                ? null
                : ToBoatDto(incident.RescueBoat, IncidentGpsBoatRoles.Rescue),
            incident.ReplacementBoat is null
                ? null
                : ToBoatDto(incident.ReplacementBoat, IncidentGpsBoatRoles.Replacement),
            IncidentGpsMissionSupport.ResolveBoatRole(incident, requestedBoatCode),
            incident.ReplacementMissionType,
            incident.ReplacementTargetStationId,
            incident.ReplacementTargetStation?.StationCode,
            incident.ReplacementTargetStation?.StationName,
            incident.ReplacementTargetStopOrder,
            incident.ReplacementTargetStation?.Latitude,
            incident.ReplacementTargetStation?.Longitude,
            incident.ReplacementDelayMinutes,
            IncidentGpsMissionSupport.ResolveAuthoritativeResumeAt(incident),
            incident.OnboardPassengerCountSnapshot,
            incident.FuturePassengerCountSnapshot,
            IncidentGpsMissionSupport.ResolveRescueNextEvents(incident),
            IncidentGpsMissionSupport.ResolveReplacementNextEvents(incident),
            canReplacementContinueTrip,
            IncidentGpsMissionSupport.CanRescueStartTowing(incident),
            canReplacementContinueTrip
                ? incident.ReplacementBoat?.Code ?? incident.Boat.Code
                : incident.Boat.Code);
    }

    private static GpsIncidentBoatDto ToBoatDto(Domain.Entities.Boat boat, string role) =>
        new(boat.Id, boat.Code, boat.Name, role);
}
