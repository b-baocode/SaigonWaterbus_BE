using SaigonWaterbus.Domain.Common;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Domain.Entities;

public class Trip : BaseGuidAuditableEntity
{
    public Guid RouteId { get; set; }
    public Guid? BoatId { get; set; }
    public string TripCode { get; set; } = null!;
    public DateOnly OperatingDate { get; set; }
    public string? ServicePeriod { get; set; }
    public DateTimeOffset DepartureTime { get; set; }
    public DateTimeOffset ArrivalTime { get; set; }
    public int CapacitySnapshot { get; set; }
    public TripStatus TripStatus { get; set; } = TripStatus.Scheduled;
    public string? StatusNote { get; set; }

    public Route Route { get; set; } = null!;
    public Boat? Boat { get; set; }
}
