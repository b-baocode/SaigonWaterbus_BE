namespace SaigonWaterbus.Domain.Enums;

/// <summary>
/// Vòng đời của khuyến mãi: mã có DÙNG được không.
/// Tách biệt với <see cref="PromotionVisibility"/> (mã có được HIỂN THỊ không).
/// </summary>
public enum PromotionStatus
{
    /// <summary>Đang soạn, chưa phát hành. Không áp dụng được.</summary>
    Draft = 0,

    /// <summary>Đã phát hành, áp dụng được (nếu còn trong hạn và còn lượt/ngân sách).</summary>
    Active = 1,

    /// <summary>Tạm ngưng, có thể bật lại. Không áp dụng được.</summary>
    Paused = 2,

    /// <summary>Kết thúc hẳn (soft delete). Không áp dụng được, không bật lại.</summary>
    Archived = 3
}
