using Microsoft.EntityFrameworkCore;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Application.Promotions;

/// <summary>
/// Danh sách khuyến mãi công khai cho khách: chỉ mã Active + Public, còn trong
/// hạn và chưa hết lượt/ngân sách. Không lộ lượt đã dùng, ngân sách hay mã Private.
/// </summary>
public sealed record GetPublicPromotionListQuery : IRequest<IReadOnlyList<PublicPromotionDto>>;

public sealed class GetPublicPromotionListQueryHandler
    : IRequestHandler<GetPublicPromotionListQuery, IReadOnlyList<PublicPromotionDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;
    private readonly TimeProvider _timeProvider;

    public GetPublicPromotionListQueryHandler(
        IApplicationDbContext context,
        IUserContext userContext,
        TimeProvider timeProvider)
    {
        _context = context;
        _userContext = userContext;
        _timeProvider = timeProvider;
    }

    public async Task<IReadOnlyList<PublicPromotionDto>> Handle(
        GetPublicPromotionListQuery request, CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();

        var rows = await _context.Set<Promotion>()
            .Where(p => p.Status == PromotionStatus.Active
                     && p.Visibility == PromotionVisibility.Public
                     && p.ValidFrom <= now
                     && p.ValidTo >= now)
            .OrderByDescending(p => p.ValidFrom)
            .Select(p => new
            {
                Promotion = p,
                // Chỉ đếm booking chưa bị giải phóng (Cancelled/Expired không chiếm lượt).
                TotalUsed = p.Bookings.Count(b => !PromotionSupport.ReleasedStatuses.Contains(b.BookingStatus)),
                BudgetSpent = p.Bookings
                    .Where(b => !PromotionSupport.ReleasedStatuses.Contains(b.BookingStatus))
                    .Sum(b => (decimal?)b.DiscountAmount) ?? 0m
            })
            .ToListAsync(cancellationToken);

        var userId = _userContext.UserId;
        Dictionary<Guid, int> userUsageByPromotionId = [];
        var hasPriorBooking = false;
        if (userId.HasValue)
        {
            var userBookings = await _context.Set<Booking>()
                .AsNoTracking()
                .Where(b => b.UserId == userId.Value)
                .ToListAsync(cancellationToken);

            hasPriorBooking = userBookings.Any();

            userUsageByPromotionId = userBookings
                .Where(b => b.PromotionId.HasValue && !PromotionSupport.ReleasedStatuses.Contains(b.BookingStatus))
                .GroupBy(b => b.PromotionId!.Value)
                .ToDictionary(g => g.Key, g => g.Count());
        }

        return rows
            .Where(r => (!r.Promotion.UsageLimit.HasValue || r.TotalUsed < r.Promotion.UsageLimit.Value)
                     && (!r.Promotion.BudgetCap.HasValue || r.BudgetSpent < r.Promotion.BudgetCap.Value)
                     && (!userId.HasValue
                         || !r.Promotion.MaxUsesPerAccount.HasValue
                         || !userUsageByPromotionId.TryGetValue(r.Promotion.Id, out var userUsed)
                         || userUsed < r.Promotion.MaxUsesPerAccount.Value)
                     && (!userId.HasValue || !r.Promotion.FirstBookingOnly || !hasPriorBooking))
            .Select(r => PromotionSupport.ToPublicDto(r.Promotion))
            .ToList();
    }
}
