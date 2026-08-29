using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Boats;
using SaigonWaterbus.Application.Common.Exceptions;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.Incidents;

public sealed record GetAvailableReplacementBoatsQuery(Guid IncidentId) : IRequest<IReadOnlyList<BoatDto>>;

public sealed class GetAvailableReplacementBoatsQueryValidator : AbstractValidator<GetAvailableReplacementBoatsQuery>
{
    public GetAvailableReplacementBoatsQueryValidator()
    {
        RuleFor(x => x.IncidentId).NotEmpty();
    }
}

public sealed class GetAvailableReplacementBoatsQueryHandler
    : IRequestHandler<GetAvailableReplacementBoatsQuery, IReadOnlyList<BoatDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;

    public GetAvailableReplacementBoatsQueryHandler(IApplicationDbContext context, IUserContext userContext)
    {
        _context = context;
        _userContext = userContext;
    }

    public async Task<IReadOnlyList<BoatDto>> Handle(
        GetAvailableReplacementBoatsQuery request,
        CancellationToken cancellationToken)
    {
        var actor = await AuthSupport.GetCurrentUserWithRoleAsync(_context, _userContext, cancellationToken);
        if (!AuthSupport.IsAdmin(actor) && !AuthSupport.IsStaff(actor) && !AuthSupport.IsManager(actor))
        {
            throw new ForbiddenAccessException();
        }

        var incident = await _context.Incidents
            .AsNoTracking()
            .Include(x => x.Trip)
            .SingleOrDefaultAsync(x => x.Id == request.IncidentId, cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy sự cố.");

        IncidentSupport.EnsureManagerCanAccessIncident(actor, incident);

        if (string.Equals(incident.ResolutionStatus, IncidentSupport.ResolvedStatus, StringComparison.OrdinalIgnoreCase))
        {
            throw new ValidationException([new FluentValidation.Results.ValidationFailure("incident",
                "Sự cố đã được xử lý, không thể điều tàu thay thế.")]);
        }

        var excludedBoatIds = new HashSet<Guid> { incident.BoatId };
        if (incident.RescueBoatId.HasValue)
        {
            excludedBoatIds.Add(incident.RescueBoatId.Value);
        }

        var candidates = await _context.Boats
            .AsNoTracking()
            .Include(x => x.Seats)
            .Where(x => x.ServiceType == BoatServiceType.Passenger
                && x.Status == BoatStatus.Active
                && !excludedBoatIds.Contains(x.Id))
            .ToListAsync(cancellationToken);

        var readyBoats = candidates
            .Where(BoatSupport.IsReadyForOperation)
            .ToList();

        var passengerImpact = await IncidentSupport.BuildPassengerImpactPlanAsync(
            _context,
            incident,
            cancellationToken);
        var nextTrips = await IncidentDispatchPlanSupport.LoadNextTripsAsync(
            _context,
            incident,
            asNoTracking: true,
            cancellationToken);
        var eligibleBoats = new List<Boat>(readyBoats.Count);
        foreach (var boat in readyBoats)
        {
            var availableSeatCount = boat.Seats.Any()
                ? boat.Seats.Count(x => x.IsActive)
                : boat.SeatCount;
            if (availableSeatCount < passengerImpact.AffectedPassengerCount)
            {
                continue;
            }

            try
            {
                await IncidentDispatchPlanSupport.EnsureReplacementBoatEligibleAsync(
                    _context,
                    boat,
                    nextTrips,
                    cancellationToken);
                eligibleBoats.Add(boat);
            }
            catch (ValidationException)
            {
                // The selection endpoint returns only boats that can satisfy the full plan.
            }
        }

        var candidateIds = eligibleBoats.Select(x => x.Id).ToArray();
        if (candidateIds.Length == 0)
        {
            return [];
        }

        var activeTripsByBoatId = await _context.Set<Trip>()
            .AsNoTracking()
            .Where(x => x.BoatId.HasValue
                && candidateIds.Contains(x.BoatId.Value)
                && x.TripStatus != TripStatus.Completed
                && x.TripStatus != TripStatus.Cancelled)
            .GroupBy(x => x.BoatId!.Value)
            .Select(g => new
            {
                BoatId = g.Key,
                Count = g.Count(),
                NextDeparture = g.Min(x => (DateTimeOffset?)x.DepartureTime)
            })
            .ToDictionaryAsync(g => g.BoatId, cancellationToken);

        return eligibleBoats
            .OrderBy(x => activeTripsByBoatId.GetValueOrDefault(x.Id)?.Count ?? 0)
            .ThenBy(x => activeTripsByBoatId.GetValueOrDefault(x.Id)?.NextDeparture
                ?? DateTimeOffset.MaxValue)
            .ThenBy(x => x.Code)
            .ThenBy(x => x.Id)
            .Select(x => BoatSupport.CreateDto(x, activeTrip: null, activeIncident: null))
            .ToArray();
    }
}
