using SaigonWaterbus.Domain.Common;

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
    public int DisplayOrder { get; set; }
    public int? RewardOption { get; set; }
    public bool IsWaterbusDefault { get; set; } = false;
}
