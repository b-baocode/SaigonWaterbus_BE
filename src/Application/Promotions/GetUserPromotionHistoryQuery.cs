using Microsoft.EntityFrameworkCore;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Application.Promotions;

/// <summary>
/// Lịch sử mã khuyến mãi đã dùng của user hiện tại.
/// </summary>
public sealed record GetUserPromotionHistoryQuery : IRequest<IReadOnlyList<UserPromotionHistoryDto>>;

public sealed class GetUserPromotionHistoryQueryHandler
    : IRequestHandler<GetUserPromotionHistoryQuery, IReadOnlyList<UserPromotionHistoryDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;
    private readonly TimeProvider _timeProvider;

    public GetUserPromotionHistoryQueryHandler(
        IApplicationDbContext context,
        IUserContext userContext,
        TimeProvider timeProvider)
    {
        _context = context;
        _userContext = userContext;
        _timeProvider = timeProvider;
    }

    public async Task<IReadOnlyList<UserPromotionHistoryDto>> Handle(
        GetUserPromotionHistoryQuery request, CancellationToken cancellationToken)
    {
        var userId = _userContext.UserId
            ?? throw new UnauthorizedAccessException("User not authenticated.");

        return await _context.Set<Domain.Entities.Booking>()
            .AsNoTracking()
            .Where(b => b.UserId == userId && b.PromotionId.HasValue)
            .Join(
                _context.Set<Domain.Entities.Promotion>(),
                b => b.PromotionId,
                p => p.Id,
                (b, p) => new { Booking = b, Promotion = p })
            .OrderByDescending(x => x.Booking.Created)
            .Select(x => new UserPromotionHistoryDto(
                x.Promotion.Id,
                x.Promotion.PromotionCode,
                x.Promotion.PromotionName,
                x.Promotion.ImageUrl,
                x.Booking.DiscountAmount,
                x.Booking.Created,
                x.Booking.BookingStatus.ToString(),
                x.Booking.Id))
            .ToListAsync(cancellationToken);
    }
}
