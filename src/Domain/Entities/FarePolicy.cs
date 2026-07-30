using SaigonWaterbus.Domain.Common;

namespace SaigonWaterbus.Domain.Entities;

/// <summary>
/// Cấu hình công thức giá vé theo quãng đường cho trip Regular (ghế STANDARD):
/// giá = RoundNearest(BaseFare + PricePerKm × km, RoundingStep),
/// sau đó áp phụ thu/hệ số loại vé và chốt giá cuối cùng về nghìn gần nhất.
/// </summary>
public class FarePolicy : BaseGuidAuditableEntity
{
    public decimal BaseFare { get; set; }
    public decimal PricePerKm { get; set; }
    public decimal RoundingStep { get; set; } = 1000m;
    public string Currency { get; set; } = "VND";
}
