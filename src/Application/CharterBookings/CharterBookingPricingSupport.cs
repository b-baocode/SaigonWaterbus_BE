using FluentValidation.Results;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.CharterBookings;

internal static class CharterBookingPricingSupport
{
    public static async Task<Promotion?> ResolvePromotionAsync(
        IApplicationDbContext context,
        string? promotionCode,
        decimal subtotal,
        DateTimeOffset now,
        string propertyName,
        bool validateMinOrder,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(promotionCode))
        {
            return null;
        }

        var normalizedCode = promotionCode.Trim().ToUpperInvariant();
        var promotion = await context.Set<Promotion>()
            .SingleOrDefaultAsync(p => p.PromotionCode == normalizedCode, cancellationToken)
            ?? throw new ValidationException([new ValidationFailure(propertyName, "Promotion code not found.")]);

        EnsurePromotionCanBeUsed(promotion, subtotal, now, propertyName, validateMinOrder);
        return promotion;
    }

    public static void EnsurePromotionCanBeUsed(
        Promotion promotion,
        decimal subtotal,
        DateTimeOffset now,
        string propertyName,
        bool validateMinOrder)
    {
        if (promotion.Status != "Active"
            || promotion.ValidFrom > now
            || promotion.ValidTo < now
            || (promotion.UsageLimit.HasValue && promotion.UsageCount >= promotion.UsageLimit))
        {
            throw new ValidationException([new ValidationFailure(propertyName,
                "Promotion code is not applicable.")]);
        }

        if (validateMinOrder && promotion.MinOrderValue.HasValue && subtotal < promotion.MinOrderValue)
        {
            throw new ValidationException([new ValidationFailure(propertyName,
                $"Minimum order value is {promotion.MinOrderValue:N0}.")]);
        }
    }

    public static decimal CalculateDiscount(Promotion? promotion, decimal subtotal)
    {
        if (promotion is null)
        {
            return 0;
        }

        var discount = promotion.PromotionType == PromotionType.Percent
            ? subtotal * promotion.DiscountValue / 100
            : promotion.DiscountValue;

        return Math.Min(discount, subtotal);
    }
}
