using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Application.Promotions;

[Authorize(Roles = "Admin")]
public sealed record GetPromotionListQuery(PromotionStatus? Status = null) : IRequest<IReadOnlyList<PromotionDto>>;

public sealed class GetPromotionListQueryHandler : IRequestHandler<GetPromotionListQuery, IReadOnlyList<PromotionDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly TimeProvider _timeProvider;

    public GetPromotionListQueryHandler(IApplicationDbContext context, TimeProvider timeProvider)
    {
        _context = context;
        _timeProvider = timeProvider;
    }

    public async Task<IReadOnlyList<PromotionDto>> Handle(GetPromotionListQuery request, CancellationToken cancellationToken)
    {
        var query = _context.Set<Promotion>().AsQueryable();

        if (request.Status.HasValue)
        {
            query = query.Where(p => p.Status == request.Status.Value);
        }

        var rows = await query
            .OrderByDescending(p => p.ValidFrom)
            .Select(p => new PromotionUsageRow(
                p,
                // Đếm tất cả booking (bao gồm cả Cancelled/Expired - không hoàn lại mã sau khi hủy).
                p.Bookings.Count(),
                p.Bookings.Sum(b => (decimal?)b.DiscountAmount) ?? 0m))
            .ToListAsync(cancellationToken);

        var now = _timeProvider.GetUtcNow();
        return rows
            .Select(r => PromotionSupport.ToDto(r.Promotion, now, r.TotalUsed, r.BudgetSpent))
            .ToList();
    }

    private sealed record PromotionUsageRow(Promotion Promotion, int TotalUsed, decimal BudgetSpent);
}
