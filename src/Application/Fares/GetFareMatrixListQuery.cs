using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Application.Fares;

public sealed record GetFareMatrixListQuery(string? RouteCode) : IRequest<IReadOnlyList<FareMatrixDto>>;

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

        if (!string.IsNullOrWhiteSpace(request.RouteCode))
        {
            var rc = request.RouteCode.Trim().ToUpperInvariant();
            query = query.Where(f => f.Route.RouteCode == rc);
        }

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
