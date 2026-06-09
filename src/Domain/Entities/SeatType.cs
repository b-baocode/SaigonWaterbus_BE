namespace SaigonWaterbus.Domain.Entities;

public class SeatType : BaseAuditableEntity
{
    public Guid WaterbusServiceId { get; set; }

    public WaterbusService WaterbusService { get; set; } = null!;

    public string Code { get; set; } = null!;

    public string Name { get; set; } = null!;

    public int DisplayOrder { get; set; }

    public bool IsActive { get; set; } = true;

    public ICollection<Seat> Seats { get; set; } = new List<Seat>();
}
