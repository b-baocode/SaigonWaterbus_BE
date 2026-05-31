namespace SaigonWaterbus.Application.Promotions;

public sealed record PromotionDto(
    Guid PromotionId,
    string PromotionCode,
    string PromotionName,
    string PromotionType,
    decimal DiscountValue,
    decimal? MinOrderValue,
    DateTimeOffset ValidFrom,
    DateTimeOffset ValidTo,
    int? UsageLimit,
    int UsageCount,
    string Status);

public sealed record PromotionValidationDto(
    bool IsValid,
    decimal DiscountAmount,
    string Message);
