namespace SaigonWaterbus.Domain.Entities;

public class ServiceSeatTypePrice : BaseAuditableEntity
{
    public Guid WaterbusServiceId { get; set; }

    public WaterbusService WaterbusService { get; set; } = null!;

    public Guid SeatTypeId { get; set; }

    public SeatType SeatType { get; set; } = null!;

    public decimal PriceModifier { get; set; } = 1m;

    public bool IsActive { get; set; } = true;
}
