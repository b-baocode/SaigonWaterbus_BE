using FluentValidation.Results;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.Promotions;

public sealed record PromotionImageFileRequest(
    string FileName,
    string? ContentType,
    long Length,
    Stream Content);

internal static class PromotionSupport
{
    /// <summary>Trạng thái booking coi như đã nhả lượt khuyến mãi (không tính vào usage/budget).</summary>
    public static readonly BookingStatus[] ReleasedStatuses =
    [
        BookingStatus.Cancelled,
        BookingStatus.Expired
    ];

    /// <summary>Các booking đang thực sự "chiếm" lượt của một mã (chưa bị nhả).</summary>
    public static IQueryable<Booking> ActiveUsageQuery(IApplicationDbContext context, Guid promotionId) =>
        context.Set<Booking>()
            .Where(b => b.PromotionId == promotionId && !ReleasedStatuses.Contains(b.BookingStatus));

    public static async Task<(int TotalUsed, decimal BudgetSpent)> GetUsageAsync(
        IApplicationDbContext context, Guid promotionId, CancellationToken cancellationToken)
    {
        var query = ActiveUsageQuery(context, promotionId);
        var totalUsed = await query.CountAsync(cancellationToken);
        var spent = await query.SumAsync(b => (decimal?)b.DiscountAmount, cancellationToken) ?? 0m;
        return (totalUsed, spent);
    }

    public static ValidationException Fail(string property, string message) =>
        new([new ValidationFailure(property, message)]);

    public static string NormalizeCode(string code) => code.Trim().ToUpperInvariant();

    public static string? NormalizeImageUrl(string? imageUrl, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(imageUrl))
        {
            return null;
        }

        var normalized = imageUrl.Trim();
        if (normalized.Length > 2048)
        {
            throw Fail(propertyName, $"{propertyName} tối đa 2048 ký tự.");
        }

        if (!Uri.TryCreate(normalized, UriKind.Absolute, out _))
        {
            throw Fail(propertyName, "ImageUrl phải là absolute URL.");
        }

        return normalized;
    }

    public static async Task<StoredPromotionImage> UploadImageAsync(
        Guid promotionId,
        PromotionImageFileRequest imageFile,
        IPromotionImageStorageService? storage,
        string propertyName,
        CancellationToken cancellationToken)
    {
        if (storage is null)
        {
            throw Fail(propertyName, "Dịch vụ lưu ảnh khuyến mãi chưa được cấu hình.");
        }

        if (string.IsNullOrWhiteSpace(imageFile.FileName))
        {
            throw Fail($"{propertyName}FileName", "Tên file ảnh là bắt buộc.");
        }

        if (imageFile.Length <= 0)
        {
            throw Fail($"{propertyName}Length", "Ảnh là bắt buộc.");
        }

        if (imageFile.Length > storage.MaxImageBytes)
        {
            throw Fail($"{propertyName}Length", $"Ảnh không được vượt quá {storage.MaxImageBytes / 1024 / 1024} MB.");
        }

        if (string.IsNullOrWhiteSpace(imageFile.ContentType)
            || !storage.AllowedImageContentTypes.Contains(imageFile.ContentType, StringComparer.OrdinalIgnoreCase))
        {
            throw Fail($"{propertyName}ContentType", "Ảnh chỉ hỗ trợ JPEG, PNG hoặc WebP.");
        }

        if (imageFile.Content.CanSeek)
        {
            imageFile.Content.Position = 0;
        }

        return await storage.UploadImageAsync(
            new PromotionImageUpload(
                promotionId,
                imageFile.Content,
                imageFile.FileName,
                imageFile.ContentType,
                Guid.NewGuid()),
            cancellationToken);
    }

    /// <summary>Chuẩn hóa scope từ DTO: bỏ list rỗng, kiểm tra tuyến tồn tại. Null nếu scope rỗng hoàn toàn.</summary>
    public static async Task<PromotionScope?> NormalizeScopeAsync(
        IApplicationDbContext context,
        PromotionScopeDto? scopeDto,
        string propertyName,
        CancellationToken cancellationToken)
    {
        if (scopeDto is null)
        {
            return null;
        }

        var scope = scopeDto.ToDomain();

        if (scope.BookingTypes is { Count: > 0 } bookingTypes)
        {
            foreach (var type in bookingTypes)
            {
                if (!string.Equals(type, Booking.SeatBookingType, StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(type, Booking.CharterBookingType, StringComparison.OrdinalIgnoreCase))
                {
                    throw Fail(propertyName, $"BookingType không hợp lệ: {type}. Hợp lệ: SeatBooking | CharterBooking.");
                }
            }
        }

        if (scope.RouteIds is { Count: > 0 } routeIds)
        {
            var distinctIds = routeIds.Distinct().ToList();
            var existing = await context.Set<Route>()
                .CountAsync(r => distinctIds.Contains(r.Id), cancellationToken);
            if (existing != distinctIds.Count)
            {
                throw Fail(propertyName, "Có tuyến (RouteId) không tồn tại trong scope.");
            }
        }

        if (scope.DepartureFrom.HasValue && scope.DepartureTo.HasValue
            && scope.DepartureFrom.Value > scope.DepartureTo.Value)
        {
            throw Fail(propertyName, "DepartureFrom phải nhỏ hơn hoặc bằng DepartureTo.");
        }

        return scope.IsEmpty ? null : scope;
    }

    public static PromotionDto ToDto(Promotion p, DateTimeOffset now, int totalUsed, decimal budgetSpent) => new(
        p.Id,
        p.PromotionCode,
        p.PromotionName,
        p.Description,
        p.PromotionType,
        p.DiscountValue,
        p.MaxDiscountAmount,
        p.MinOrderValue,
        p.ValidFrom,
        p.ValidTo,
        p.UsageLimit,
        p.MaxUsesPerAccount,
        p.BudgetCap,
        p.FirstBookingOnly,
        PromotionScopeDto.FromDomain(p.Scope),
        p.Visibility,
        p.Status,
        PromotionEffectiveState.Compute(p.Status, p.ValidFrom, p.ValidTo, now, totalUsed, p.UsageLimit, budgetSpent, p.BudgetCap),
        totalUsed,
        budgetSpent,
        p.ImageUrl);

    public static PublicPromotionDto ToPublicDto(Promotion p) => new(
        p.PromotionCode,
        p.PromotionName,
        p.Description,
        p.PromotionType,
        p.DiscountValue,
        p.MaxDiscountAmount,
        p.MinOrderValue,
        p.ValidFrom,
        p.ValidTo,
        p.ImageUrl);
}
