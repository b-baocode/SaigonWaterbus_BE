using SaigonWaterbus.Domain.Common;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Domain.Entities;

public class Promotion : BaseGuidAuditableEntity
{
    public string PromotionCode { get; set; } = null!;
    public string PromotionName { get; set; } = null!;
    public string? Description { get; set; }

    public PromotionType PromotionType { get; set; }
    public decimal DiscountValue { get; set; }

    /// <summary>Trần số tiền giảm (chỉ có ý nghĩa với Percent). Null = không trần.</summary>
    public decimal? MaxDiscountAmount { get; set; }

    /// <summary>Giá trị đơn tối thiểu để áp dụng. Null = không yêu cầu.</summary>
    public decimal? MinOrderValue { get; set; }

    public DateTimeOffset ValidFrom { get; set; }
    public DateTimeOffset ValidTo { get; set; }

    /// <summary>Tổng lượt dùng tối đa của mã. Null = không giới hạn.</summary>
    public int? UsageLimit { get; set; }

    /// <summary>Số lượt tối đa mỗi tài khoản. Null = không giới hạn.</summary>
    public int? MaxUsesPerAccount { get; set; }

    /// <summary>Trần tổng số tiền được giảm qua mã này. Null = không giới hạn ngân sách.</summary>
    public decimal? BudgetCap { get; set; }

    /// <summary>Chỉ áp cho booking đầu tiên của tài khoản.</summary>
    public bool FirstBookingOnly { get; set; }

    /// <summary>Phạm vi áp dụng (tuyến / loại booking / khung giờ). Null = áp mọi nơi.</summary>
    public PromotionScope? Scope { get; set; }

    public PromotionVisibility Visibility { get; set; } = PromotionVisibility.Public;
    public PromotionStatus Status { get; set; } = PromotionStatus.Draft;

    public string? ImageUrl { get; set; }
    public string? ImagePublicId { get; set; }

    public ICollection<Booking> Bookings { get; set; } = new List<Booking>();

    /// <summary>
    /// Nguồn sự thật DUY NHẤT cho công thức tính giảm giá: áp trần giảm, kẹp theo
    /// subtotal, và làm tròn về VND nguyên. Không kiểm tra điều kiện áp dụng (min
    /// order, hạn dùng, lượt, scope) — việc đó ở PromotionEligibilitySupport.
    /// </summary>
    public decimal CalculateDiscount(decimal subtotal)
    {
        if (subtotal <= 0)
        {
            return 0;
        }

        var raw = PromotionType == PromotionType.Percent
            ? subtotal * DiscountValue / 100m
            : DiscountValue;

        if (MaxDiscountAmount.HasValue)
        {
            raw = Math.Min(raw, MaxDiscountAmount.Value);
        }

        raw = Math.Min(raw, subtotal);

        return Math.Round(raw, 0, MidpointRounding.AwayFromZero);
    }
}
