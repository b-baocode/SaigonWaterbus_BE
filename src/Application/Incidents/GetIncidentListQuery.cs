using SaigonWaterbus.Application.Common.Interfaces;

namespace SaigonWaterbus.Application.Incidents;

public sealed record GetIncidentListQuery(
    Guid? BoatId,
    Guid? TripId,
    string? ResolutionStatus) : IRequest<IReadOnlyList<IncidentDto>>;

public sealed class GetIncidentListQueryHandler : IRequestHandler<GetIncidentListQuery, IReadOnlyList<IncidentDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;

    public GetIncidentListQueryHandler(IApplicationDbContext context, IUserContext userContext)
    {
        _context = context;
        _userContext = userContext;
    }

    public async Task<IReadOnlyList<IncidentDto>> Handle(
        GetIncidentListQuery request,
        CancellationToken cancellationToken)
    {
        await IncidentSupport.EnsureCurrentUserCanReportIncidentAsync(_context, _userContext, cancellationToken);

        var query = _context.Incidents
            .Include(x => x.Boat)
            .Include(x => x.Trip)
            .Include(x => x.Reporter)
            .Include(x => x.AssignedManager)
            .Include(x => x.AssignedByUser)
            .Include(x => x.ReplacementBoat)
            .Include(x => x.ReplacementAssignedByUser)
            .Include(x => x.Resolver)
            .AsNoTracking();

        if (request.BoatId.HasValue)
        {
            query = query.Where(x => x.BoatId == request.BoatId.Value);
        }

        if (request.TripId.HasValue)
        {
            query = query.Where(x => x.TripId == request.TripId.Value);
        }

        if (!string.IsNullOrWhiteSpace(request.ResolutionStatus))
        {
            var status = request.ResolutionStatus.Trim();
            query = query.Where(x => x.ResolutionStatus == status);
        }

        var incidents = await query
            .OrderByDescending(x => x.OccurredAt)
            .ThenByDescending(x => x.Id)
            .ToListAsync(cancellationToken);

        return incidents.Select(IncidentSupport.ToDto).ToList();
    }
}
