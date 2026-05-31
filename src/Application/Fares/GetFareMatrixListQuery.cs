using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Application.Fares;

public sealed record GetFareMatrixListQuery(Guid? RouteId) : IRequest<IReadOnlyList<FareMatrixDto>>;

public sealed class GetFareMatrixListQueryHandler : IRequestHandler<GetFareMatrixListQuery, IReadOnlyList<FareMatrixDto>>
{
    private readonly IApplicationDbContext _context;

    public GetFareMatrixListQueryHandler(IApplicationDbContext context) => _context = context;

    public async Task<IReadOnlyList<FareMatrixDto>> Handle(GetFareMatrixListQuery request, CancellationToken cancellationToken)
    {
        var query = _context.Set<FareMatrix>()
            .Include(f => f.FromStation)
            .Include(f => f.ToStation)
            .AsQueryable();

        if (request.RouteId.HasValue)
            query = query.Where(f => f.RouteId == request.RouteId.Value);

        return await query
            .OrderBy(f => f.FromStation.StationName)
            .ThenBy(f => f.ToStation.StationName)
            .Select(f => new FareMatrixDto(f.Id, f.RouteId,
                f.FromStation.Id, f.FromStation.StationName,
                f.ToStation.Id, f.ToStation.StationName,
                f.BasePrice, f.IsActive))
            .ToListAsync(cancellationToken);
    }
}
