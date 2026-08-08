using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Exceptions;
using SaigonWaterbus.Application.Common.Interfaces;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;

namespace SaigonWaterbus.Application.Incidents;

public sealed record GetIncidentDetailQuery(Guid IncidentId) : IRequest<IncidentDto>;

public sealed class GetIncidentDetailQueryHandler : IRequestHandler<GetIncidentDetailQuery, IncidentDto>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;

    public GetIncidentDetailQueryHandler(IApplicationDbContext context, IUserContext userContext)
    {
        _context = context;
        _userContext = userContext;
    }

    public async Task<IncidentDto> Handle(GetIncidentDetailQuery request, CancellationToken cancellationToken)
    {
        var actor = await AuthSupport.GetCurrentUserWithRoleAsync(_context, _userContext, cancellationToken);
        if (!AuthSupport.IsAdmin(actor) && !AuthSupport.IsStaff(actor) && !AuthSupport.IsManager(actor))
        {
            throw new ForbiddenAccessException();
        }

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
            .Include(x => x.ReplacementTargetStation)
            .Include(x => x.Resolver)
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == request.IncidentId, cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy sự cố.");

        IncidentSupport.EnsureManagerCanAccessIncident(actor, incident);

        var activeTicketCount = incident.TripId.HasValue
            ? await IncidentSupport.CountActiveTicketsAsync(_context, incident.TripId.Value, cancellationToken)
            : 0;
        return IncidentSupport.ToDto(incident, activeTicketCount);
    }
}
