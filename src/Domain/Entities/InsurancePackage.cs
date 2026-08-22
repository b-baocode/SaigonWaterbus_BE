using SaigonWaterbus.Domain.Common;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Domain.Entities;

public class InsurancePackage : BaseGuidAuditableEntity
{
    public string Code { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string BookingType { get; set; } = Booking.CharterBookingType;
    public bool IsRequired { get; set; } = true;
    public string? ProviderName { get; set; }
    public string? ProviderLogoUrl { get; set; }
    public string? ImageUrl { get; set; }
    public decimal UnitPremiumAmount { get; set; }
    public decimal CoverageAmount { get; set; }
    public string Currency { get; set; } = "VND";
    public string[] Conditions { get; set; } = [];
    public string? TermsUrl { get; set; }
    public bool IsActive { get; set; } = true;
    public int? RewardOption { get; set; }
    public bool IsWaterbusDefault { get; set; } = false;

    /// <summary>
    /// Nguồn gốc gói bảo hiểm: <c>Waterbus</c> (mặc định, hệ thống tự gắn vào giá)
    /// hoặc <c>ThirdParty</c> (tuỳ chọn, khách tự chọn). Backfill an toàn từ
    /// <see cref="IsWaterbusDefault"/> cho dữ liệu cũ.
    /// </summary>
    public InsuranceProviderSource ProviderSource { get; set; } = InsuranceProviderSource.ThirdParty;
}
