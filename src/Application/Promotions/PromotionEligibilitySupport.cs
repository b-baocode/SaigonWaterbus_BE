using FluentValidation.Results;
using SaigonWaterbus.Application.Common;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.Promotions;

/// <summary>
/// Ngữ cảnh của đơn đang xét, dùng để so khớp <see cref="PromotionScope"/>.
/// Dimension nào null thì không so khớp theo chiều đó (mã vẫn được coi là hợp lệ
/// theo chiều đó) — cho phép endpoint validate chạy khi chưa có thông tin chuyến.
/// </summary>
public sealed record PromotionApplyContext(
    string? BookingType = null,
    Guid? RouteId = null,
    DayOfWeek? DepartureDay = null,
    TimeOnly? DepartureTime = null);

public sealed record PromotionEligibility(bool IsValid, decimal Discount, string? Reason);

/// <summary>
/// Nguồn sự thật DUY NHẤT cho việc kiểm tra điều kiện áp dụng khuyến mãi. Mọi
/// luồng (đặt vé lẻ, charter, thanh toán, endpoint validate) đều đi qua đây thay
/// vì tự lặp lại logic. Lượt dùng và ngân sách được suy ra trực tiếp từ bảng
/// bookings — không có counter để lệch.
/// </summary>
public static class PromotionEligibilitySupport
{
    /// <summary>Bản throw: dùng trong luồng đặt vé/charter/thanh toán. Trả về số tiền giảm hoặc ném ValidationException.</summary>
    public static async Task<decimal> EnsureAndCalculateAsync(
        IApplicationDbContext context,
        Promotion promotion,
        Guid? userId,
        decimal subtotal,
        DateTimeOffset now,
        string propertyName,
        PromotionApplyContext? applyContext = null,
        Guid? excludedBookingId = null,
        CancellationToken cancellationToken = default)
    {
        var result = await EvaluateAsync(
            context, promotion, userId, subtotal, now, applyContext, excludedBookingId, cancellationToken);

        if (!result.IsValid)
        {
            throw new ValidationException([new ValidationFailure(propertyName, result.Reason)]);
        }

        return result.Discount;
    }

    /// <summary>Bản non-throw: dùng cho endpoint validate. Trả về (IsValid, Discount, Reason).</summary>
    public static async Task<PromotionEligibility> EvaluateAsync(
        IApplicationDbContext context,
        Promotion promotion,
        Guid? userId,
        decimal subtotal,
        DateTimeOffset now,
        PromotionApplyContext? applyContext = null,
        Guid? excludedBookingId = null,
        CancellationToken cancellationToken = default)
    {
        if (promotion.Status != PromotionStatus.Active)
        {
            return Invalid("Khuyến mãi không khả dụng.");
        }

        if (promotion.ValidFrom > now)
        {
            return Invalid("Khuyến mãi chưa bắt đầu.");
        }

        if (promotion.ValidTo < now)
        {
            return Invalid("Khuyến mãi đã hết hạn.");
        }

        if (promotion.MinOrderValue.HasValue && subtotal < promotion.MinOrderValue.Value)
        {
            return Invalid($"Giá trị đơn tối thiểu là {promotion.MinOrderValue.Value:N0}.");
        }

        if (!MatchesScope(promotion.Scope, applyContext))
        {
            return Invalid("Khuyến mãi không áp dụng cho chuyến/tuyến này.");
        }

        var discount = PriceRoundingSupport.RoundFare(promotion.CalculateDiscount(subtotal));

        // Lượt dùng suy ra từ bookings đang chiếm lượt (không tính Cancelled/Expired).
        var usedBookings = userId.HasValue
            ? context.Set<Booking>()
                .Where(b => b.PromotionId == promotion.Id
                    && b.UserId == userId.Value
                    && !PromotionSupport.ReleasedStatuses.Contains(b.BookingStatus)
                    && (!excludedBookingId.HasValue || b.Id != excludedBookingId.Value))
            : context.Set<Booking>()
                .Where(b => b.PromotionId == promotion.Id
                    && !PromotionSupport.ReleasedStatuses.Contains(b.BookingStatus)
                    && (!excludedBookingId.HasValue || b.Id != excludedBookingId.Value));

        if (promotion.UsageLimit.HasValue)
        {
            var totalUsed = await usedBookings.CountAsync(cancellationToken);
            if (totalUsed >= promotion.UsageLimit.Value)
            {
                return Invalid("Khuyến mãi đã hết lượt sử dụng.");
            }
        }

        if (promotion.BudgetCap.HasValue)
        {
            var usedForBudget = context.Set<Booking>()
                .Where(b => b.PromotionId == promotion.Id
                    && !PromotionSupport.ReleasedStatuses.Contains(b.BookingStatus)
                    && (!excludedBookingId.HasValue || b.Id != excludedBookingId.Value));
            var spent = await usedForBudget.SumAsync(b => (decimal?)b.DiscountAmount, cancellationToken) ?? 0m;
            if (spent + discount > promotion.BudgetCap.Value)
            {
                return Invalid("Khuyến mãi đã hết ngân sách.");
            }
        }

        if (userId.HasValue)
        {
            if (promotion.MaxUsesPerAccount.HasValue)
            {
                var userUsed = await usedBookings.CountAsync(cancellationToken);
                if (userUsed >= promotion.MaxUsesPerAccount.Value)
                {
                    return Invalid(promotion.MaxUsesPerAccount.Value == 1
                        ? "Khuyến mãi này mỗi tài khoản chỉ được sử dụng 1 lần."
                        : $"Khuyến mãi này mỗi tài khoản chỉ được dùng tối đa {promotion.MaxUsesPerAccount.Value} lần.");
                }
            }

            if (promotion.FirstBookingOnly)
            {
                var hasPriorBooking = await context.Set<Booking>()
                    .AnyAsync(b => b.UserId == userId.Value
                                && (!excludedBookingId.HasValue || b.Id != excludedBookingId.Value),
                        cancellationToken);
                if (hasPriorBooking)
                {
                    return Invalid("Khuyến mãi chỉ dành cho lần đặt đầu tiên.");
                }
            }
        }

        return new PromotionEligibility(true, discount, null);
    }

    public static bool ReleasesPromotionUsage(BookingStatus status) => PromotionSupport.ReleasedStatuses.Contains(status);

    private static PromotionEligibility Invalid(string reason) => new(false, 0, reason);

    private static bool MatchesScope(PromotionScope? scope, PromotionApplyContext? ctx)
    {
        if (scope is null || scope.IsEmpty)
        {
            return true;
        }

        if (ctx is null)
        {
            // Không có ngữ cảnh chuyến để đối chiếu (vd endpoint validate) — không phủ định.
            return true;
        }

        if (scope.BookingTypes is { Count: > 0 } types
            && ctx.BookingType is not null
            && !types.Contains(ctx.BookingType, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        if (scope.RouteIds is { Count: > 0 } routes
            && ctx.RouteId.HasValue
            && !routes.Contains(ctx.RouteId.Value))
        {
            return false;
        }

        if (scope.DaysOfWeek is { Count: > 0 } days
            && ctx.DepartureDay.HasValue
            && !days.Contains(ctx.DepartureDay.Value))
        {
            return false;
        }

        if (ctx.DepartureTime.HasValue)
        {
            if (scope.DepartureFrom.HasValue && ctx.DepartureTime.Value < scope.DepartureFrom.Value)
            {
                return false;
            }

            if (scope.DepartureTo.HasValue && ctx.DepartureTime.Value > scope.DepartureTo.Value)
            {
                return false;
            }
        }

        return true;
    }
}
