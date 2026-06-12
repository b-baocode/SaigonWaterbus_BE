using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;

namespace SaigonWaterbus.Application.Stations;

public sealed record GetStationDetailQuery(Guid StationId) : IRequest<StationDto>;

public sealed class GetStationDetailQueryHandler : IRequestHandler<GetStationDetailQuery, StationDto>
{
    private readonly IApplicationDbContext _context;

    public GetStationDetailQueryHandler(IApplicationDbContext context) => _context = context;

    public async Task<StationDto> Handle(GetStationDetailQuery request, CancellationToken cancellationToken)
    {
        var station = await _context.Set<Station>()
            .Include(s => s.UserAssignments.Where(a => a.IsActive))
                .ThenInclude(a => a.User)
                    .ThenInclude(u => u.Role)
            .SingleOrDefaultAsync(s => s.Id == request.StationId, cancellationToken)
            ?? throw new NotFoundException("Station not found.");

        return StationDto.From(station);
    }
}
