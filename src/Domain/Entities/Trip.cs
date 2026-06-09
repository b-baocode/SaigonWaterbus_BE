using SaigonWaterbus.Domain.Common;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Domain.Entities;

public class Trip : BaseGuidAuditableEntity
{
    public Guid RouteId { get; set; }
    public string TripCode { get; set; } = null!;
    public DateOnly OperatingDate { get; set; }
    public DateTimeOffset DepartureTime { get; set; }
    public DateTimeOffset ArrivalTime { get; set; }
    public int CapacitySnapshot { get; set; }
    public TripStatus TripStatus { get; set; } = TripStatus.Scheduled;
    public string? StatusNote { get; set; }

    public Route Route { get; set; } = null!;
    public ICollection<TripStop> TripStops { get; set; } = new List<TripStop>();
    public ICollection<Incident> Incidents { get; set; } = new List<Incident>();
    public ICollection<BookingItem> BookingItems { get; set; } = new List<BookingItem>();
}
