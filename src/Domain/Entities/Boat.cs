using SaigonWaterbus.Domain.Common;

namespace SaigonWaterbus.Domain.Entities;

public class Boat : BaseGuidAuditableEntity
{
    public string BoatCode { get; set; } = null!;
    public string BoatName { get; set; } = null!;
    public int Capacity { get; set; }
    public string BoatStatus { get; set; } = "Active";
    public string? Description { get; set; }

    public ICollection<Seat> Seats { get; set; } = new List<Seat>();
    public ICollection<Trip> Trips { get; set; } = new List<Trip>();
}
