using SaigonWaterbus.Domain.Common;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Domain.Entities;

public class Trip : BaseGuidAuditableEntity
{
    public Guid RouteId { get; set; }
    public Guid? BoatId { get; set; }
    public string TripCode { get; set; } = null!;
    public string TripType { get; set; } = TripTypes.Regular;
    public Guid? SourceBookingId { get; set; }
    public DateOnly OperatingDate { get; set; }
    public string? ServicePeriod { get; set; }
    public DateTimeOffset DepartureTime { get; set; }
    public DateTimeOffset ArrivalTime { get; set; }
    public int DelayMinutes { get; set; }
    public string? DelayReason { get; set; }
    public DateTimeOffset? DelayStartedAt { get; set; }
    public DateTimeOffset? DelayEndedAt { get; set; }
    public int? DelayStartStopOrder { get; set; }
    public int DelayPropagationMinutes { get; set; }
    public DateTimeOffset? AdjustedDepartureTime { get; set; }
    public DateTimeOffset? AdjustedArrivalTime { get; set; }
    public int CapacitySnapshot { get; set; }
    public TripStatus TripStatus { get; set; } = TripStatus.Scheduled;
    public string? StatusNote { get; set; }
    public DateTimeOffset? LastStatusChangedAt { get; set; }

    public Route Route { get; set; } = null!;
    public Boat? Boat { get; set; }
    public ICollection<Incident> Incidents { get; set; } = new List<Incident>();
    public ICollection<TripSeat> TripSeats { get; set; } = new List<TripSeat>();
    public ICollection<TripStop> TripStops { get; set; } = new List<TripStop>();
}
