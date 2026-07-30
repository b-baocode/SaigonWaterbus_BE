using FluentValidation.Results;
using SaigonWaterbus.Application.Common;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Application.Promotions;
using SaigonWaterbus.Domain.Entities;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.CharterBookings;

internal static class CharterBookingPricingSupport
{
    public static async Task<Promotion?> ResolvePromotionAsync(
        IApplicationDbContext context,
        string? promotionCode,
        Guid? userId,
        decimal subtotal,
        DateTimeOffset now,
        string propertyName,
        Guid? excludedBookingId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(promotionCode))
        {
            return null;
        }

        var normalizedCode = PromotionSupport.NormalizeCode(promotionCode);
        var promotion = await context.Set<Promotion>()
            .SingleOrDefaultAsync(p => p.PromotionCode == normalizedCode, cancellationToken)
            ?? throw new ValidationException([new ValidationFailure(propertyName, "Không tìm thấy mã khuyến mãi.")]);

        await EnsurePromotionCanBeUsedAsync(context, promotion, userId, subtotal, now, propertyName, excludedBookingId, cancellationToken);
        return promotion;
    }

    public static async Task EnsurePromotionCanBeUsedAsync(
        IApplicationDbContext context,
        Promotion promotion,
        Guid? userId,
        decimal subtotal,
        DateTimeOffset now,
        string propertyName,
        Guid? excludedBookingId,
        CancellationToken cancellationToken)
    {
        await PromotionEligibilitySupport.EnsureAndCalculateAsync(
            context,
            promotion,
            userId,
            subtotal,
            now,
            propertyName,
            new PromotionApplyContext(Booking.CharterBookingType),
            excludedBookingId,
            cancellationToken);
    }

    public static decimal CalculateDiscount(Promotion? promotion, decimal subtotal) =>
        PriceRoundingSupport.RoundFare(promotion?.CalculateDiscount(subtotal) ?? 0);
}
