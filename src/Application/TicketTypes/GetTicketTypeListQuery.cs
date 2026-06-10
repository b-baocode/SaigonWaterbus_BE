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
        return await _context.Set<TicketType>()
            .Where(tt => tt.IsActive)
            .OrderBy(tt => tt.TicketTypeCode)
            .Select(tt => new TicketTypeDto(tt.Id, tt.TicketTypeCode, tt.TicketTypeName,
                tt.Description, tt.PriceModifier, tt.PointsEarnedRate, tt.IsActive))
            .ToListAsync(cancellationToken);
    }
}
