using NetTopologySuite.Geometries;
using SaigonWaterbus.Domain.Common;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Domain.Entities;

public class Station : BaseGuidAuditableEntity
{
    public string StationCode { get; set; } = null!;
    public string StationName { get; set; } = null!;
    public string? Address { get; set; }
    public string? Description { get; set; }
    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }
    public StationStatus Status { get; set; } = StationStatus.Active;
    public Point? Location { get; set; }
    public string? OsmId { get; set; }
    public string? ImageUrl { get; set; }
    public string[] ImageUrls { get; set; } = [];
    public string? ImagePublicId { get; set; }
    public bool HasWaitingArea { get; set; }
    public bool HasParking { get; set; }
    public bool HasTicketCounter { get; set; }

    public ICollection<RouteStop> RouteStops { get; set; } = new List<RouteStop>();
    public ICollection<Landmark> Landmarks { get; set; } = new List<Landmark>();
    public ICollection<UserStationAssignment> UserAssignments { get; set; } = new List<UserStationAssignment>();
}
