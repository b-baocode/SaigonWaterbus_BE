namespace SaigonWaterbus.Infrastructure.Ai;

public sealed class GeminiOptions
{
    public const string SectionName = "Gemini";

    /// <summary>API key lấy từ Google AI Studio (aistudio.google.com). Dạng "AIza...".</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Tên model. Bản Flash đủ cho chatbox routing và rẻ/free. Google đổi version
    /// thường xuyên — kiểm tra tên model hiện hành trên docs rồi chỉnh ở appsettings.
    /// </summary>
    public string Model { get; set; } = "gemini-2.0-flash";

    public string ApiBaseUrl { get; set; } = "https://generativelanguage.googleapis.com/v1beta";

    public int MaxOutputTokens { get; set; } = 2048;
}
