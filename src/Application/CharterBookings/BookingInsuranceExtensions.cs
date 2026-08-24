using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Application.CharterBookings;

/// <summary>
/// Extension truy cập nhanh các nhóm bảo hiểm trong <see cref="Booking.InsuranceSnapshots"/>.
/// Booking có thể mang:
///
///   - 1 snapshot Waterbus default (auto-attach khi có hành khách) — <c>IsWaterbusDefault == true</c>.
///   - N snapshot ThirdParty do khách chọn thêm — <c>IsWaterbusDefault == false</c>.
///
/// Tổng tiền bảo hiểm = tổng <c>TotalAmount</c> của tất cả snapshot trong list.
/// </summary>
public static class BookingInsuranceExtensions
{
    /// <summary>Snapshot gói mặc định Waterbus (auto-attach). Null nếu khách tắt.</summary>
    public static BookingInsuranceSnapshot? GetDefaultInsurance(this Booking booking) =>
        (booking.InsuranceSnapshots ?? new List<BookingInsuranceSnapshot>())
            .FirstOrDefault(s => s.IsWaterbusDefault);

    /// <summary>Danh sách gói ThirdParty khách chọn thêm (không bao gồm default).</summary>
    public static IReadOnlyList<BookingInsuranceSnapshot> GetOptionalInsurances(this Booking booking) =>
        (booking.InsuranceSnapshots ?? new List<BookingInsuranceSnapshot>())
            .Where(s => !s.IsWaterbusDefault)
            .ToList();

    /// <summary>Tổng tiền gói mặc định Waterbus (0 nếu không có).</summary>
    public static decimal GetDefaultInsuranceAmount(this Booking booking) =>
        (booking.InsuranceSnapshots ?? new List<BookingInsuranceSnapshot>())
            .Where(s => s.IsWaterbusDefault)
            .Sum(s => s.TotalAmount);

    /// <summary>Tổng tiền các gói ThirdParty khách chọn thêm (0 nếu không có).</summary>
    public static decimal GetOptionalInsuranceAmount(this Booking booking) =>
        (booking.InsuranceSnapshots ?? new List<BookingInsuranceSnapshot>())
            .Where(s => !s.IsWaterbusDefault)
            .Sum(s => s.TotalAmount);

    /// <summary>Tổng tiền bảo hiểm (default + optional).</summary>
    public static decimal GetTotalInsuranceAmount(this Booking booking) =>
        (booking.InsuranceSnapshots ?? new List<BookingInsuranceSnapshot>())
            .Sum(s => s.TotalAmount);

    /// <summary>Khách có chọn gói ThirdParty nào không.</summary>
    public static bool HasOptionalInsurance(this Booking booking) =>
        (booking.InsuranceSnapshots ?? new List<BookingInsuranceSnapshot>())
            .Any(s => !s.IsWaterbusDefault);
}