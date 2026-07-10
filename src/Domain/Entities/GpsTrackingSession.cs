namespace SaigonWaterbus.Domain.Entities;

public class GpsTrackingSession : BaseGuidAuditableEntity
{
    public Guid GpsDeviceId { get; set; }

    public Guid BoatId { get; set; }

    public Guid? RouteId { get; set; }

    public string? RouteCode { get; set; }

    public string? RouteName { get; set; }

    public decimal? PlannedLengthMeters { get; set; }

    public Guid? TripId { get; set; }

    public Guid? StartStationId { get; set; }

    public Guid? EndStationId { get; set; }

    public string Mode { get; set; } = "route-survey";

    public string Status { get; set; } = "Recording";

    public DateTimeOffset StartedAt { get; set; }

    public DateTimeOffset? StoppedAt { get; set; }

    public Boat Boat { get; set; } = null!;

    public GpsDevice GpsDevice { get; set; } = null!;

    public Route? Route { get; set; }

    public Trip? Trip { get; set; }

    public Station? StartStation { get; set; }

    public Station? EndStation { get; set; }

    public ICollection<GpsTrackPoint> TrackPoints { get; set; } = new List<GpsTrackPoint>();
}
