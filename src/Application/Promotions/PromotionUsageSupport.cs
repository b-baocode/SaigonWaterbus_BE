using FluentValidation.Results;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.Promotions;

internal static class PromotionUsageSupport
{
    private static readonly BookingStatus[] ReleasedStatuses =
    [
        BookingStatus.Cancelled,
        BookingStatus.Expired,
        BookingStatus.Refunded
    ];

    public static async Task EnsureAccountCanUsePromotionAsync(
        IApplicationDbContext context,
        Promotion promotion,
        Guid userId,
        string propertyName,
        CancellationToken cancellationToken,
        Guid? excludedBookingId = null)
    {
        if (promotion.AccountUsagePolicy != PromotionAccountUsagePolicy.OncePerAccount)
        {
            return;
        }

        var alreadyUsed = await context.Set<Booking>()
            .AnyAsync(x => x.UserId == userId
                        && x.PromotionId == promotion.Id
                        && !ReleasedStatuses.Contains(x.BookingStatus)
                        && (!excludedBookingId.HasValue || x.Id != excludedBookingId.Value),
                cancellationToken);

        if (alreadyUsed)
        {
            throw new ValidationException([new ValidationFailure(
                propertyName,
                "Promotion này mỗi tài khoản chỉ được sử dụng 1 lần.")]);
        }
    }

    public static void IncrementUsage(Promotion? promotion)
    {
        if (promotion is not null)
        {
            promotion.UsageCount++;
        }
    }

    public static void DecrementUsage(Promotion? promotion)
    {
        if (promotion is not null)
        {
            promotion.UsageCount = Math.Max(0, promotion.UsageCount - 1);
        }
    }

    public static bool ReleasesPromotionUsage(BookingStatus status) =>
        ReleasedStatuses.Contains(status);
}
