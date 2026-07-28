using SaigonWaterbus.Domain.Common;

namespace SaigonWaterbus.Domain.Entities;

/// <summary>
/// Một bản thu thuyết minh của một <see cref="Landmark"/> ở một <see cref="Voice"/> cụ thể.
/// Mỗi (landmark × giọng) đúng một dòng — audio đã pre-bake và up sẵn.
/// </summary>
public class LandmarkAudio : BaseGuidAuditableEntity
{
    public Guid LandmarkId { get; set; }
    public Guid VoiceId { get; set; }
    public string AudioUrl { get; set; } = null!;

    /// <summary>Độ dài audio (giây) — để app canh thời điểm phát so với tốc độ tàu.</summary>
    public decimal? DurationSeconds { get; set; }

    public Landmark Landmark { get; set; } = null!;
    public Voice Voice { get; set; } = null!;
}
