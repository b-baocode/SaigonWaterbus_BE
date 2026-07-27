using SaigonWaterbus.Domain.Common;

namespace SaigonWaterbus.Domain.Entities;

/// <summary>
/// Điểm thuyết minh cảnh quan, định danh bằng toạ độ (không gắn tuyến). App tải toàn bộ
/// landmark active rồi tự phát khi tàu vào trong bán kính <see cref="TriggerRadiusMeters"/>.
/// Một điểm dùng chung cho mọi tuyến đi ngang qua — bake audio một lần.
/// </summary>
public class Landmark : BaseGuidAuditableEntity
{
    public string LandmarkName { get; set; } = null!;
    public string? Description { get; set; }
    public decimal Latitude { get; set; }
    public decimal Longitude { get; set; }

    /// <summary>Thứ tự hiển thị ở màn admin (không phải thứ tự phát — phát theo vị trí thực).</summary>
    public int DisplayOrder { get; set; }

    /// <summary>Bán kính (mét) tàu vào tới thì kích hoạt phát. Mặc định 300m.</summary>
    public int TriggerRadiusMeters { get; set; } = 300;

    public bool IsActive { get; set; } = true;

    public ICollection<LandmarkAudio> Audios { get; set; } = new List<LandmarkAudio>();
}
