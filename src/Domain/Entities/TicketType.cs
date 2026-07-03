using SaigonWaterbus.Domain.Common;

namespace SaigonWaterbus.Domain.Entities;

public class TicketType : BaseGuidEntity
{
    public string Code { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public decimal PriceModifier { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Comma-separated seat type codes this ticket applies to.
    /// Null means unrestricted (all seat types allowed).
    /// </summary>
    public string? AllowedSeatTypeCodes { get; set; }

    public bool IsApplicableForSeatType(string seatTypeCode) =>
        AllowedSeatTypeCodes is null ||
        AllowedSeatTypeCodes.Split(',').Any(c =>
            c.Equals(seatTypeCode, StringComparison.OrdinalIgnoreCase));

    public IReadOnlyList<string>? GetAllowedSeatTypeCodesList() =>
        AllowedSeatTypeCodes?.Split(',', StringSplitOptions.RemoveEmptyEntries);
}
