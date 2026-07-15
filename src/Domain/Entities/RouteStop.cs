using SaigonWaterbus.Domain.Common;

namespace SaigonWaterbus.Domain.Entities;

public class RouteStop : BaseGuidAuditableEntity
{
    public Guid RouteId { get; set; }
    public Guid StationId { get; set; }
    public int StopOrder { get; set; }
    public int? StandardTravelMin { get; set; }

    /// <summary>
    /// Quãng đường (km) từ trạm liền trước theo lộ trình sông; null cho trạm đầu tuyến
    /// hoặc khi admin chưa nhập. Dùng để tính giá vé theo quãng đường trên trip Regular.
    /// </summary>
    public decimal? DistanceFromPreviousKm { get; set; }
    public bool IsPickupAllowed { get; set; } = true;
    public bool IsDropoffAllowed { get; set; } = true;

    public Route Route { get; set; } = null!;
    public Station Station { get; set; } = null!;
}
