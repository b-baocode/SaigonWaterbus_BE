namespace SaigonWaterbus.Domain.Enums;

/// <summary>
/// Nguồn gốc của gói bảo hiểm: phân biệt gói mặc định do Waterbus cung cấp
/// (auto-attach vào tổng giá, khách không cần chọn) vs gói của bên thứ 3
/// (tuỳ chọn, khách tự chọn).
/// </summary>
public enum InsuranceProviderSource
{
    /// <summary>Gói bảo hiểm do Waterbus cung cấp — mặc định, hệ thống tự gắn vào giá.</summary>
    Waterbus = 1,

    /// <summary>Gói bảo hiểm của bên thứ 3 — tuỳ chọn, khách hàng tự chọn.</summary>
    ThirdParty = 2
}