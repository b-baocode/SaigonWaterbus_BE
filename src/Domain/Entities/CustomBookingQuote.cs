namespace SaigonWaterbus.Domain.Entities;

public class CustomBookingQuote : BaseGuidAuditableEntity
{
    public Guid CustomBookingRequestId { get; set; }

    public CustomBookingRequest CustomBookingRequest { get; set; } = null!;

    public decimal QuotedPrice { get; set; }

    public decimal DepositAmount { get; set; }

    public decimal RemainingAmount { get; set; }

    public string Currency { get; set; } = "VND";

    public string? PriceNote { get; set; }

    public DateTimeOffset? ValidUntil { get; set; }
}
