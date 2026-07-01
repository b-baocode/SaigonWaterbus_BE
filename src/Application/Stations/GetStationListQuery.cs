using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Application.Stations;

public sealed record GetStationListQuery : IRequest<IReadOnlyList<StationDto>>;

public sealed class GetStationListQueryHandler : IRequestHandler<GetStationListQuery, IReadOnlyList<StationDto>>
{
    private readonly IApplicationDbContext _context;

    public GetStationListQueryHandler(IApplicationDbContext context) => _context = context;

    public async Task<IReadOnlyList<StationDto>> Handle(GetStationListQuery request, CancellationToken cancellationToken)
    {
        var stations = await _context.Set<Station>()
            .Include(s => s.UserAssignments)
                .ThenInclude(a => a.User)
                    .ThenInclude(u => u.Role)
            .Where(s => s.Status == StationStatus.Active)
            .OrderBy(s => s.StationName)
            .ToListAsync(cancellationToken);

        return stations
            .Select(StationDto.From)
            .ToArray();
    }
}
