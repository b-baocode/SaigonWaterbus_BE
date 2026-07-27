using SaigonWaterbus.Domain.Common;

namespace SaigonWaterbus.Domain.Entities;

/// <summary>
/// Giọng đọc mà khách có thể chọn để nghe thuyết minh. Là danh mục hiển thị cho
/// khách; <see cref="VieneuVoiceId"/> là tên giọng dùng khi pre-bake audio bằng VieNeu-TTS.
/// </summary>
public class Voice : BaseGuidAuditableEntity
{
    /// <summary>Tên hiển thị cho khách, vd "Giọng Nam - kể chuyện".</summary>
    public string Name { get; set; } = null!;

    /// <summary>Miền: Bắc | Trung | Nam.</summary>
    public string? Region { get; set; }

    /// <summary>Giới tính: Nam | Nữ.</summary>
    public string? Gender { get; set; }

    /// <summary>Phong cách: tự nhiên | tin tức | kể chuyện.</summary>
    public string? Style { get; set; }

    /// <summary>
    /// Tên preset VieNeu-TTS (1 trong 14 giọng sẵn). Null nếu là giọng clone —
    /// khi đó bake dùng <see cref="RefAudioUrl"/>. Phải có ÍT NHẤT một trong hai.
    /// </summary>
    public string? VieneuVoiceId { get; set; }

    /// <summary>Clip audio mẫu (3–8s) để clone giọng lúc bake. Null nếu dùng preset.</summary>
    public string? RefAudioUrl { get; set; }

    /// <summary>Audio mẫu để khách nghe thử trước khi chọn.</summary>
    public string? SampleUrl { get; set; }

    public bool IsActive { get; set; } = true;
    public int DisplayOrder { get; set; }

    public ICollection<LandmarkAudio> LandmarkAudios { get; set; } = new List<LandmarkAudio>();
}
