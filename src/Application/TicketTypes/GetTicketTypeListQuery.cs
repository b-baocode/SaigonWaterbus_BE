using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Application.TicketTypes;

public sealed record GetTicketTypeListQuery : IRequest<IReadOnlyList<TicketTypeDto>>;

public sealed class GetTicketTypeListQueryHandler : IRequestHandler<GetTicketTypeListQuery, IReadOnlyList<TicketTypeDto>>
{
    private readonly IApplicationDbContext _context;

    public GetTicketTypeListQueryHandler(IApplicationDbContext context) => _context = context;

    public async Task<IReadOnlyList<TicketTypeDto>> Handle(GetTicketTypeListQuery request, CancellationToken cancellationToken)
    {
        var types = await _context.Set<TicketType>()
            .AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.Code)
            .ToListAsync(cancellationToken);

        return types.Select(x => new TicketTypeDto(
            x.Id,
            x.Code,
            x.Name,
            x.Description,
            x.PriceModifier,
            x.IsActive,
            x.GetAllowedSeatTypeCodesList())).ToList();
    }
}
