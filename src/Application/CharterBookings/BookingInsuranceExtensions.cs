using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Application.CharterBookings;

/// <summary>
/// Extension truy cập nhanh các nhóm bảo hiểm trong <see cref="Booking.InsuranceSnapshots"/>.
///
/// Booking charter (luôn chọn 1 gói):
///   - 1 snapshot duy nhất: có thể là Waterbus default HOẶC ThirdParty do khách chọn.
///   - Field <c>IsWaterbusDefault</c> chỉ là metadata phân biệt nguồn gói (BE nhận biết
///     được đó có phải default hay không), KHÔNG còn ý nghĩa stacking.
///
/// Tổng tiền bảo hiểm = <c>TotalAmount</c> của snapshot duy nhất.
/// </summary>
public static class BookingInsuranceExtensions
{
    /// <summary>
    /// Snapshot duy nhất khách đã chọn cho charter (Waterbus default hoặc ThirdParty).
    /// Null nếu không có hành khách hoặc booking không yêu cầu bảo hiểm.
    /// </summary>
    public static BookingInsuranceSnapshot? GetSelectedInsurance(this Booking booking) =>
        (booking.InsuranceSnapshots ?? new List<BookingInsuranceSnapshot>())
            .FirstOrDefault();

    /// <summary>
    /// Snapshot gói Waterbus default nếu booking có chọn gói đó. Null nếu chọn ThirdParty
    /// hoặc không có. Dùng cho logic tính tổng tiền riêng phần default (legacy).
    /// </summary>
    public static BookingInsuranceSnapshot? GetDefaultInsurance(this Booking booking) =>
        (booking.InsuranceSnapshots ?? new List<BookingInsuranceSnapshot>())
            .FirstOrDefault(s => s.IsWaterbusDefault);

    /// <summary>Tổng tiền bảo hiểm của booking (= tổng tất cả snapshot, thường là 1).</summary>
    public static decimal GetTotalInsuranceAmount(this Booking booking) =>
        (booking.InsuranceSnapshots ?? new List<BookingInsuranceSnapshot>())
            .Sum(s => s.TotalAmount);

    /// <summary>Tổng tiền gói Waterbus default (0 nếu khách chọn ThirdParty).</summary>
    public static decimal GetDefaultInsuranceAmount(this Booking booking) =>
        (booking.InsuranceSnapshots ?? new List<BookingInsuranceSnapshot>())
            .Where(s => s.IsWaterbusDefault)
            .Sum(s => s.TotalAmount);

    /// <summary>
    /// Backward-compat shim: trả về danh sách các snapshot KHÔNG phải default.
    /// Với charter flow mới, danh sách này sẽ rỗng vì chỉ có 1 snapshot duy nhất.
    /// </summary>
    public static IReadOnlyList<BookingInsuranceSnapshot> GetOptionalInsurances(this Booking booking) =>
        (booking.InsuranceSnapshots ?? new List<BookingInsuranceSnapshot>())
            .Where(s => !s.IsWaterbusDefault)
            .ToList();

    /// <summary>
    /// Backward-compat: luôn trả <c>false</c> với charter flow mới (chỉ có 1 snapshot duy nhất,
    /// không có stacking). Giữ method để code cũ vẫn compile.
    /// </summary>
    public static bool HasOptionalInsurance(this Booking booking) => false;
}