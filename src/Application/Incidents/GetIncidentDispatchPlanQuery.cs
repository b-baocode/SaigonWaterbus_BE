using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Exceptions;
using SaigonWaterbus.Application.Common.Interfaces;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;

namespace SaigonWaterbus.Application.Incidents;

public sealed record GetIncidentDispatchPlanQuery(Guid IncidentId) : IRequest<IncidentDispatchPlanDto>;

public sealed class GetIncidentDispatchPlanQueryValidator : AbstractValidator<GetIncidentDispatchPlanQuery>
{
    public GetIncidentDispatchPlanQueryValidator() => RuleFor(x => x.IncidentId).NotEmpty();
}

public sealed class GetIncidentDispatchPlanQueryHandler
    : IRequestHandler<GetIncidentDispatchPlanQuery, IncidentDispatchPlanDto>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;

    public GetIncidentDispatchPlanQueryHandler(IApplicationDbContext context, IUserContext userContext)
    {
        _context = context;
        _userContext = userContext;
    }

    public async Task<IncidentDispatchPlanDto> Handle(
        GetIncidentDispatchPlanQuery request,
        CancellationToken cancellationToken)
    {
        var actor = await AuthSupport.GetCurrentUserWithRoleAsync(_context, _userContext, cancellationToken);
        if (!AuthSupport.IsAdmin(actor) && !AuthSupport.IsStaff(actor) && !AuthSupport.IsManager(actor))
        {
            throw new ForbiddenAccessException();
        }

        var incident = await _context.Incidents
            .AsNoTracking()
            .Include(x => x.Boat)
            .Include(x => x.Trip)
            .SingleOrDefaultAsync(x => x.Id == request.IncidentId, cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy sự cố.");
        IncidentSupport.EnsureManagerCanAccessIncident(actor, incident);

        var passengerImpact = await IncidentSupport.BuildPassengerImpactPlanAsync(
            _context,
            incident,
            cancellationToken);
        var nextTrips = await IncidentDispatchPlanSupport.LoadNextTripsAsync(
            _context,
            incident,
            asNoTracking: true,
            cancellationToken);
        var ticketCounts = await IncidentSupport.CountActiveTicketsByTripAsync(
            _context,
            nextTrips.Select(x => x.Id).ToArray(),
            cancellationToken);

        return new IncidentDispatchPlanDto(
            incident.Id,
            incident.BoatId,
            incident.Boat.Code,
            incident.TripId,
            incident.Trip?.TripCode,
            passengerImpact.ActiveTicketCount,
            passengerImpact.OnboardPassengerCount,
            passengerImpact.FuturePassengerCount,
            passengerImpact.AffectedPassengerCount > 0,
            nextTrips.Count > 0,
            nextTrips.Count,
            nextTrips.Count == 0 ? 0 : nextTrips.Max(x => x.CapacitySnapshot),
            passengerImpact.AffectedPassengerCount > 0 || nextTrips.Count > 0,
            nextTrips.Select(x => new IncidentNextTripDto(
                x.Id,
                x.TripCode,
                x.OperatingDate,
                x.DepartureTime,
                x.ArrivalTime,
                IncidentDispatchPlanSupport.ResolveDeparture(x),
                IncidentDispatchPlanSupport.ResolveArrival(x),
                x.TripStatus,
                x.CapacitySnapshot,
                ticketCounts.GetValueOrDefault(x.Id))).ToArray());
    }
}
