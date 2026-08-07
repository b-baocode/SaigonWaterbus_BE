namespace SaigonWaterbus.Infrastructure.Ai;

public sealed class GoogleTextToSpeechOptions
{
    public const string SectionName = "GoogleTextToSpeech";

    /// <summary>
    /// API key của Google Cloud Text-to-Speech. KHÁC key Gemini: key này phải được bật
    /// "Cloud Text-to-Speech API" trong Google Cloud Console và project phải có billing.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    public string ApiBaseUrl { get; set; } = "https://texttospeech.googleapis.com/v1";

    /// <summary>
    /// Giọng đọc tiếng Việt. Danh sách giọng Google đổi theo thời gian — tra
    /// `voices.list` rồi chỉnh ở appsettings, đừng sửa code.
    /// </summary>
    public string VietnameseVoice { get; set; } = "vi-VN-Wavenet-A";

    public string EnglishVoice { get; set; } = "en-US-Neural2-F";

    /// <summary>
    /// Tốc độ đọc (1.0 = bình thường). Hướng dẫn viên nói hơi chậm lại một chút thì dễ
    /// nghe hơn khi có tiếng động cơ tàu.
    /// </summary>
    public double SpeakingRate { get; set; } = 0.95;

    /// <summary>Cắt cụt text quá dài trước khi gửi — chặn hoá đơn bất ngờ (tính theo ký tự).</summary>
    public int MaxCharacters { get; set; } = 1000;

    public int TimeoutSeconds { get; set; } = 20;
}
