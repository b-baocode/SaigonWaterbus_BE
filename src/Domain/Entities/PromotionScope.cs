namespace SaigonWaterbus.Domain.Entities;

/// <summary>
/// Phạm vi áp dụng của khuyến mãi. Lưu dạng jsonb trên cột promotions.scope
/// (cùng pattern với <see cref="BookingInsuranceSnapshot"/>). Mọi field null =
/// không giới hạn theo chiều đó. Scope null hoàn toàn = áp dụng mọi nơi.
/// </summary>
public sealed class PromotionScope
{
    /// <summary>Chỉ áp cho các loại booking này (Booking.SeatBookingType / CharterBookingType). Null = mọi loại.</summary>
    public IReadOnlyList<string>? BookingTypes { get; set; }

    /// <summary>Chỉ áp cho các tuyến này. Null = mọi tuyến. Không có FK — validate tồn tại lúc tạo/sửa.</summary>
    public IReadOnlyList<Guid>? RouteIds { get; set; }

    /// <summary>Chỉ áp cho chuyến khởi hành vào các thứ này (mã off-peak T2–T6). Null = mọi ngày.</summary>
    public IReadOnlyList<DayOfWeek>? DaysOfWeek { get; set; }

    /// <summary>Chỉ áp cho chuyến khởi hành từ giờ này. Null = không giới hạn cận dưới.</summary>
    public TimeOnly? DepartureFrom { get; set; }

    /// <summary>Chỉ áp cho chuyến khởi hành đến giờ này. Null = không giới hạn cận trên.</summary>
    public TimeOnly? DepartureTo { get; set; }

    public bool IsEmpty =>
        (BookingTypes is null || BookingTypes.Count == 0)
        && (RouteIds is null || RouteIds.Count == 0)
        && (DaysOfWeek is null || DaysOfWeek.Count == 0)
        && DepartureFrom is null
        && DepartureTo is null;
}
