using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Application.Promotions;

public sealed record PromotionDto(
    Guid PromotionId,
    string PromotionCode,
    string PromotionName,
    PromotionType PromotionType,
    decimal DiscountValue,
    decimal? MinOrderValue,
    DateTimeOffset ValidFrom,
    DateTimeOffset ValidTo,
    int? UsageLimit,
    int UsageCount,
    PromotionAccountUsagePolicy AccountUsagePolicy,
    string Status);

public sealed record PromotionValidationDto(
    bool IsValid,
    decimal DiscountAmount,
    string Message);
