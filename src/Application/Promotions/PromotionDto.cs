using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Application.Promotions;

public sealed record PromotionScopeDto(
    IReadOnlyList<string>? BookingTypes,
    IReadOnlyList<Guid>? RouteIds,
    IReadOnlyList<DayOfWeek>? DaysOfWeek,
    TimeOnly? DepartureFrom,
    TimeOnly? DepartureTo)
{
    public static PromotionScopeDto? FromDomain(PromotionScope? scope) =>
        scope is null || scope.IsEmpty
            ? null
            : new PromotionScopeDto(scope.BookingTypes, scope.RouteIds, scope.DaysOfWeek, scope.DepartureFrom, scope.DepartureTo);

    public PromotionScope ToDomain() => new()
    {
        BookingTypes = BookingTypes,
        RouteIds = RouteIds,
        DaysOfWeek = DaysOfWeek,
        DepartureFrom = DepartureFrom,
        DepartureTo = DepartureTo
    };
}

/// <summary>DTO đầy đủ cho admin. TotalUsed/BudgetSpent suy ra từ bookings; EffectiveState tính lúc đọc.</summary>
public sealed record PromotionDto(
    Guid PromotionId,
    string PromotionCode,
    string PromotionName,
    string? Description,
    PromotionType PromotionType,
    decimal DiscountValue,
    decimal? MaxDiscountAmount,
    decimal? MinOrderValue,
    DateTimeOffset ValidFrom,
    DateTimeOffset ValidTo,
    int? UsageLimit,
    int? MaxUsesPerAccount,
    decimal? BudgetCap,
    bool FirstBookingOnly,
    PromotionScopeDto? Scope,
    PromotionVisibility Visibility,
    PromotionStatus Status,
    string EffectiveState,
    int TotalUsed,
    decimal BudgetSpent,
    string? ImageUrl);

/// <summary>DTO gọn cho khách: không lộ lượt đã dùng, ngân sách, mã Private.</summary>
public sealed record PublicPromotionDto(
    string PromotionCode,
    string PromotionName,
    string? Description,
    PromotionType PromotionType,
    decimal DiscountValue,
    decimal? MaxDiscountAmount,
    decimal? MinOrderValue,
    DateTimeOffset ValidFrom,
    DateTimeOffset ValidTo,
    string? ImageUrl);

/// <summary>DTO lịch sử mã KM đã dùng của user.</summary>
public sealed record UserPromotionHistoryDto(
    Guid PromotionId,
    string PromotionCode,
    string PromotionName,
    string? ImageUrl,
    decimal DiscountAmount,
    DateTimeOffset UsedAt,
    string BookingStatus,
    Guid BookingId);

public sealed record PromotionValidationDto(
    bool IsValid,
    decimal DiscountAmount,
    string Message);
