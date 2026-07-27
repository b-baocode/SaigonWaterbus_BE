using SaigonWaterbus.Domain.Common;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Domain.Entities;

public class CharterBoatRentalPricePolicy : BaseGuidAuditableEntity
{
    public int NumberOfDecks { get; set; }

    public BoatRentalUnit RentalUnit { get; set; }

    public decimal UnitPrice { get; set; }

    public string Currency { get; set; } = "VND";
}
