namespace SaigonWaterbus.Application.TicketTypes;

public sealed record GetTicketTypeListQuery : IRequest<IReadOnlyList<TicketTypeDto>>;

public sealed class GetTicketTypeListQueryHandler : IRequestHandler<GetTicketTypeListQuery, IReadOnlyList<TicketTypeDto>>
{
    public Task<IReadOnlyList<TicketTypeDto>> Handle(GetTicketTypeListQuery request, CancellationToken cancellationToken)
    {
        IReadOnlyList<TicketTypeDto> result = TicketTypeCatalog.ActiveDefinitions
            .Select(x => new TicketTypeDto(
                x.TicketTypeId,
                x.TicketTypeCode,
                x.TicketTypeName,
                x.Description,
                x.PriceModifier,
                x.PointsEarnedRate,
                x.IsActive,
                x.DisplayOrder,
                x.Category))
            .ToList();

        return Task.FromResult(result);
    }
}
