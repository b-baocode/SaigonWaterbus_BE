using SaigonWaterbus.Domain.Common;

namespace SaigonWaterbus.Domain.Entities;

/// <summary>
/// Cấu hình công thức giá vé theo quãng đường cho trip Regular (ghế STANDARD):
/// giá = RoundUp(BaseFare + PricePerKm × km, RoundingStep), tối thiểu MinFare nếu có,
/// sau đó nhân hệ số loại vé (ADULT/INFANT/...). Chỉ một policy active tại một thời điểm.
/// </summary>
public class FarePolicy : BaseGuidAuditableEntity
{
    public decimal BaseFare { get; set; }
    public decimal PricePerKm { get; set; }
    public decimal RoundingStep { get; set; } = 1000m;
    public decimal? MinFare { get; set; }
    public string Currency { get; set; } = "VND";
    public bool IsActive { get; set; } = true;
}
