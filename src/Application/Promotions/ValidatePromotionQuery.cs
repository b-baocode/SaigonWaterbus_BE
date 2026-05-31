using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Application.Promotions;

public sealed record ValidatePromotionQuery(string Code, decimal SubtotalAmount) : IRequest<PromotionValidationDto>;

public sealed class ValidatePromotionQueryHandler : IRequestHandler<ValidatePromotionQuery, PromotionValidationDto>
{
    private readonly IApplicationDbContext _context;
    private readonly TimeProvider _timeProvider;

    public ValidatePromotionQueryHandler(IApplicationDbContext context, TimeProvider timeProvider)
    {
        _context = context;
        _timeProvider = timeProvider;
    }

    public async Task<PromotionValidationDto> Handle(ValidatePromotionQuery request, CancellationToken cancellationToken)
    {
        var code = request.Code.Trim().ToUpperInvariant();
        var now = _timeProvider.GetUtcNow();

        var promotion = await _context.Set<Promotion>()
            .SingleOrDefaultAsync(p => p.PromotionCode == code, cancellationToken);

        if (promotion is null)
            return new PromotionValidationDto(false, 0, "Promotion code not found.");

        if (promotion.Status != "Active")
            return new PromotionValidationDto(false, 0, "Promotion is not active.");

        if (promotion.ValidFrom > now || promotion.ValidTo < now)
            return new PromotionValidationDto(false, 0, "Promotion is not within valid date range.");

        if (promotion.UsageLimit.HasValue && promotion.UsageCount >= promotion.UsageLimit)
            return new PromotionValidationDto(false, 0, "Promotion usage limit has been reached.");

        if (promotion.MinOrderValue.HasValue && request.SubtotalAmount < promotion.MinOrderValue)
            return new PromotionValidationDto(false, 0, $"Minimum order value is {promotion.MinOrderValue:N0}.");

        var discount = promotion.PromotionType == "Percent"
            ? request.SubtotalAmount * promotion.DiscountValue / 100
            : Math.Min(promotion.DiscountValue, request.SubtotalAmount);

        return new PromotionValidationDto(true, discount, "Promotion applied successfully.");
    }
}
