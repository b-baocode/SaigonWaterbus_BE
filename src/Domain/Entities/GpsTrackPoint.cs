namespace SaigonWaterbus.Domain.Entities;

public class GpsTrackPoint : BaseGuidAuditableEntity
{
    public Guid SessionId { get; set; }

    public Guid GpsDeviceId { get; set; }

    public Guid BoatId { get; set; }

    public Guid? RouteId { get; set; }

    public Guid? TripId { get; set; }

    public Guid MessageId { get; set; }

    public decimal Latitude { get; set; }

    public decimal Longitude { get; set; }

    public decimal? SpeedKmh { get; set; }

    public int? Heading { get; set; }

    public decimal? AccuracyMeters { get; set; }

    public DateTimeOffset RecordedAt { get; set; }

    public DateTimeOffset ReceivedAt { get; set; }

    public long Sequence { get; set; }

    public string Status { get; set; } = "unknown";

    public string? Direction { get; set; }

    public int? BatteryPercent { get; set; }

    public int? SignalStrength { get; set; }

    public string? GpsFixQuality { get; set; }

    public GpsTrackingSession Session { get; set; } = null!;

    public Boat Boat { get; set; } = null!;

    public GpsDevice GpsDevice { get; set; } = null!;

    public Route? Route { get; set; }

    public Trip? Trip { get; set; }
}
