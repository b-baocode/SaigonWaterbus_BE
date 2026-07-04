using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Application.Promotions;

public sealed record GetPromotionListQuery(string? Status = null) : IRequest<IReadOnlyList<PromotionDto>>;

public sealed class GetPromotionListQueryHandler : IRequestHandler<GetPromotionListQuery, IReadOnlyList<PromotionDto>>
{
    private readonly IApplicationDbContext _context;

    public GetPromotionListQueryHandler(IApplicationDbContext context) => _context = context;

    public async Task<IReadOnlyList<PromotionDto>> Handle(GetPromotionListQuery request, CancellationToken cancellationToken)
    {
        var query = _context.Set<Promotion>().AsQueryable();

        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            query = query.Where(p => p.Status == request.Status.Trim());
        }

        return await query
            .OrderByDescending(p => p.Created)
            .Select(p => new PromotionDto(
                p.Id, p.PromotionCode, p.PromotionName, p.PromotionType,
                p.DiscountValue, p.MinOrderValue, p.ValidFrom, p.ValidTo,
                p.UsageLimit, p.UsageCount, p.AccountUsagePolicy, p.Status))
            .ToListAsync(cancellationToken);
    }
}
