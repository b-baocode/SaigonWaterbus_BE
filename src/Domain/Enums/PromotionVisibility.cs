namespace SaigonWaterbus.Domain.Enums;

/// <summary>
/// Khả kiến của khuyến mãi: khách có THẤY mã trên trang khuyến mãi không.
/// Độc lập với <see cref="PromotionStatus"/>: mã Private vẫn áp dụng được bình
/// thường nếu khách biết mã (nhận qua email/SMS), chỉ không được liệt kê công khai.
/// </summary>
public enum PromotionVisibility
{
    /// <summary>Hiển thị công khai trên trang khuyến mãi.</summary>
    Public = 0,

    /// <summary>Mã bí mật, chỉ ai được gửi mới biết. Vẫn dùng được nếu nhập đúng mã.</summary>
    Private = 1
}
