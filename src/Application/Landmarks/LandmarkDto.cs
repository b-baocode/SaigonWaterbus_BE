using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Application.Landmarks;

public sealed record VoiceDto(
    Guid VoiceId,
    string Name,
    string? Region,
    string? Gender,
    string? Style,
    string? SampleUrl)
{
    public static VoiceDto From(Voice v) =>
        new(v.Id, v.Name, v.Region, v.Gender, v.Style, v.SampleUrl);
}

/// <summary>DTO cho admin — lộ thêm vieneuVoiceId, isActive, displayOrder.</summary>
public sealed record VoiceAdminDto(
    Guid VoiceId,
    string Name,
    string? Region,
    string? Gender,
    string? Style,
    string? VieneuVoiceId,
    string? RefAudioUrl,
    string? SampleUrl,
    bool IsActive,
    int DisplayOrder)
{
    public static VoiceAdminDto From(Voice v) =>
        new(v.Id, v.Name, v.Region, v.Gender, v.Style, v.VieneuVoiceId,
            v.RefAudioUrl, v.SampleUrl, v.IsActive, v.DisplayOrder);
}

/// <summary>Một bản thu đã bake của landmark (dùng ở màn admin để biết giọng nào đã có audio).</summary>
public sealed record LandmarkAudioDto(
    Guid LandmarkAudioId,
    Guid VoiceId,
    string VoiceName,
    string AudioUrl,
    decimal? DurationSeconds)
{
    public static LandmarkAudioDto From(LandmarkAudio a) =>
        new(a.Id, a.VoiceId, a.Voice?.Name ?? string.Empty, a.AudioUrl, a.DurationSeconds);
}

/// <summary>DTO admin cho landmark — kèm danh sách audio đã bake theo từng giọng.</summary>
public sealed record LandmarkAdminDto(
    Guid LandmarkId,
    string LandmarkName,
    string? Description,
    decimal Latitude,
    decimal Longitude,
    int DisplayOrder,
    int TriggerRadiusMeters,
    bool IsActive,
    IReadOnlyCollection<LandmarkAudioDto> Audios)
{
    public static LandmarkAdminDto From(Landmark l) =>
        new(l.Id, l.LandmarkName, l.Description, l.Latitude, l.Longitude,
            l.DisplayOrder, l.TriggerRadiusMeters, l.IsActive,
            l.Audios.Select(LandmarkAudioDto.From).OrderBy(a => a.VoiceName).ToList());
}

/// <summary>Bản thu của một landmark ở đúng giọng khách chọn.</summary>
public sealed record LandmarkDto(
    Guid LandmarkId,
    string LandmarkName,
    string? Description,
    decimal Latitude,
    decimal Longitude,
    int DisplayOrder,
    int TriggerRadiusMeters,
    Guid? VoiceId,
    string? AudioUrl,
    decimal? DurationSeconds)
{
    /// <summary>Dựng DTO cho một giọng cụ thể (đã lọc audio theo voiceId ở query).</summary>
    public static LandmarkDto From(Landmark l, LandmarkAudio? audio) =>
        new(l.Id, l.LandmarkName, l.Description, l.Latitude, l.Longitude,
            l.DisplayOrder, l.TriggerRadiusMeters,
            audio?.VoiceId, audio?.AudioUrl, audio?.DurationSeconds);
}
