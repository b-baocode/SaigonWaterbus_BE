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

    /// <summary>
    /// Model dùng để nghe audio → text (hướng dẫn viên bằng giọng nói). Tách khỏi
    /// <see cref="Model"/> vì không phải model nào cũng nhận audio input — đổi được model chat
    /// mà không vô tình làm hỏng STT, và ngược lại.
    /// </summary>
    public string SpeechToTextModel { get; set; } = "gemini-flash-lite-latest";

    public int MaxOutputTokens { get; set; } = 2048;

    /// <summary>
    /// Ngân sách token "suy nghĩ" (thinking). Chatbox chỉ định tuyến tool nên cần rất ít —
    /// đặt budget NHỎ (mặc định 128) để giảm mạnh độ trễ (từ ~1-2 phút xuống vài giây)
    /// thay vì để model tự quyết (dynamic = chậm).
    /// Lưu ý: 0 (tắt hẳn) chỉ vài model chấp nhận — gemini-flash-latest TỪ CHỐI 0, nên dùng
    /// số dương nhỏ. Đặt -1 để bỏ hẳn thinkingConfig (model tự quyết, chậm hơn).
    /// </summary>
    public int ThinkingBudget { get; set; } = 128;
}
