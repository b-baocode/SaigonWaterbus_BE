using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Application.Routes;

public sealed record WaterwaySegmentDto(
    Guid Id,
    string? OsmId,
    string? WaterwayName,
    string WaterwayType,
    decimal TotalLengthKm);

public sealed record GetWaterwayListQuery(
    string? Name = null,
    string? Type = null) : IRequest<IReadOnlyList<WaterwaySegmentDto>>;

public sealed class GetWaterwayListQueryHandler : IRequestHandler<GetWaterwayListQuery, IReadOnlyList<WaterwaySegmentDto>>
{
    private readonly IApplicationDbContext _context;

    public GetWaterwayListQueryHandler(IApplicationDbContext context) => _context = context;

    public async Task<IReadOnlyList<WaterwaySegmentDto>> Handle(GetWaterwayListQuery request, CancellationToken cancellationToken)
    {
        var query = _context.Set<WaterwaySegment>().AsQueryable();

        if (!string.IsNullOrWhiteSpace(request.Name))
        {
            var nameLower = request.Name.Trim().ToLowerInvariant();
            query = query.Where(s => s.WaterwayName != null && s.WaterwayName.ToLower().Contains(nameLower));
        }

        if (!string.IsNullOrWhiteSpace(request.Type))
        {
            var typeLower = request.Type.Trim().ToLowerInvariant();
            query = query.Where(s => s.WaterwayType == typeLower);
        }

        var segments = await query.ToListAsync(cancellationToken);

        return segments
            .GroupBy(s => new { s.OsmId, s.WaterwayName, s.WaterwayType })
            .Select(g => new WaterwaySegmentDto(
                g.First().Id,
                g.Key.OsmId,
                g.Key.WaterwayName,
                g.Key.WaterwayType,
                g.Sum(s => s.LengthKm)))
            .OrderBy(d => d.WaterwayType)
            .ThenBy(d => d.WaterwayName)
            .ToList();
    }
}
