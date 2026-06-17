using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Application.Routes;

public sealed record WaterwayCoordinateDto(double Longitude, double Latitude);

public sealed record WaterwaySegmentDetailDto(int SegmentOrder, decimal LengthKm, IReadOnlyList<WaterwayCoordinateDto> Coordinates);

public sealed record WaterwayDetailDto(
    string? OsmId,
    string? WaterwayName,
    string WaterwayType,
    decimal TotalLengthKm,
    int SegmentCount,
    IReadOnlyList<WaterwaySegmentDetailDto> Segments);

public sealed record GetWaterwayDetailQuery(Guid Id) : IRequest<WaterwayDetailDto?>;

public sealed class GetWaterwayDetailQueryHandler : IRequestHandler<GetWaterwayDetailQuery, WaterwayDetailDto?>
{
    private readonly IApplicationDbContext _context;

    public GetWaterwayDetailQueryHandler(IApplicationDbContext context) => _context = context;

    public async Task<WaterwayDetailDto?> Handle(GetWaterwayDetailQuery request, CancellationToken cancellationToken)
    {
        var anchor = await _context.Set<WaterwaySegment>()
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == request.Id, cancellationToken);

        if (anchor is null) return null;

        var siblings = await _context.Set<WaterwaySegment>()
            .AsNoTracking()
            .Where(s => s.OsmId == anchor.OsmId && s.WaterwayName == anchor.WaterwayName && s.WaterwayType == anchor.WaterwayType)
            .OrderBy(s => s.SegmentOrder)
            .ToListAsync(cancellationToken);

        var segmentDtos = siblings.Select(s => new WaterwaySegmentDetailDto(
            s.SegmentOrder,
            s.LengthKm,
            s.Geometry.Coordinates
                .Select(c => new WaterwayCoordinateDto(c.X, c.Y))
                .ToList()))
            .ToList();

        return new WaterwayDetailDto(
            anchor.OsmId,
            anchor.WaterwayName,
            anchor.WaterwayType,
            siblings.Sum(s => s.LengthKm),
            siblings.Count,
            segmentDtos);
    }
}
