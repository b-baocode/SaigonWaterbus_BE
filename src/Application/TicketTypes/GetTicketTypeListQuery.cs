namespace SaigonWaterbus.Application.TicketTypes;

public sealed record GetTicketTypeListQuery : IRequest<IReadOnlyList<TicketTypeDto>>;

public sealed class GetTicketTypeListQueryHandler : IRequestHandler<GetTicketTypeListQuery, IReadOnlyList<TicketTypeDto>>
{
    public Task<IReadOnlyList<TicketTypeDto>> Handle(GetTicketTypeListQuery request, CancellationToken cancellationToken)
    {
        IReadOnlyList<TicketTypeDto> result = TicketTypePricing.All
            .OrderBy(x => x.Code)
            .Select(x => new TicketTypeDto(
                x.Code,
                x.Name,
                x.Description,
                x.PriceModifier,
                x.GetAllowedSeatTypeCodesList()))
            .ToList();

        return Task.FromResult(result);
    }
}
