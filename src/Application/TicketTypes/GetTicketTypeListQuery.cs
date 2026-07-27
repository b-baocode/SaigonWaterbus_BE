using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Application.TicketTypes;

public sealed record GetTicketTypeListQuery : IRequest<IReadOnlyList<TicketTypeDto>>;

public sealed class GetTicketTypeListQueryHandler : IRequestHandler<GetTicketTypeListQuery, IReadOnlyList<TicketTypeDto>>
{
    private readonly IApplicationDbContext _context;

    public GetTicketTypeListQueryHandler(IApplicationDbContext context) => _context = context;

    public async Task<IReadOnlyList<TicketTypeDto>> Handle(
        GetTicketTypeListQuery request,
        CancellationToken cancellationToken)
    {
        var configuredRules = await TicketFareRuleSupport.LoadConfiguredRulesAsync(_context, cancellationToken);

        IReadOnlyList<TicketTypeDto> result = TicketTypePricing.All
            .OrderBy(x => x.Code)
            .Select(x => new TicketTypeDto(
                x.Code,
                x.Name,
                x.Description,
                TicketFareRuleSupport.ResolveEffectivePriceModifier(configuredRules, x, RouteTypes.Regular),
                x.GetAllowedSeatTypeCodesList(),
                TicketFareRuleSupport.ResolveEffectivePriceModifier(configuredRules, x, RouteTypes.SightseeingLoop)))
            .ToList();

        return result;
    }
}
