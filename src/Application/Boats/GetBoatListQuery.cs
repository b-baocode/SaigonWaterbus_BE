using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Application.Boats;

public sealed record GetBoatListQuery : IRequest<IReadOnlyList<BoatDto>>;

public sealed class GetBoatListQueryHandler : IRequestHandler<GetBoatListQuery, IReadOnlyList<BoatDto>>
{
    private readonly IApplicationDbContext _context;

    public GetBoatListQueryHandler(IApplicationDbContext context) => _context = context;

    public async Task<IReadOnlyList<BoatDto>> Handle(GetBoatListQuery request, CancellationToken cancellationToken)
    {
        return await _context.Set<Boat>()
            .Where(b => b.BoatStatus == "Active")
            .OrderBy(b => b.BoatCode)
            .Select(b => new BoatDto(b.Id, b.BoatCode, b.BoatName, b.Capacity, b.BoatStatus, b.Description))
            .ToListAsync(cancellationToken);
    }
}
