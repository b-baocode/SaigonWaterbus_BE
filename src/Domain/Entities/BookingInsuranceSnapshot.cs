namespace SaigonWaterbus.Domain.Entities;

public class BookingInsuranceSnapshot
{
    public Guid InsurancePackageId { get; set; }
    public string Code { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string BookingType { get; set; } = Booking.CharterBookingType;
    public bool IsRequired { get; set; }
    public string? ProviderName { get; set; }
    public string? ProviderLogoUrl { get; set; }
    public string? ImageUrl { get; set; }
    public decimal UnitPremiumAmount { get; set; }
    public decimal CoverageAmount { get; set; }
    public string Currency { get; set; } = "VND";
    public string[] Conditions { get; set; } = [];
    public string? TermsUrl { get; set; }
    public int Quantity { get; set; }
    public decimal TotalAmount { get; set; }
    public DateTimeOffset QuotedAt { get; set; }
}
