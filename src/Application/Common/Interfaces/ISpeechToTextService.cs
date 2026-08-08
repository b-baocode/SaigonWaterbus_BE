namespace SaigonWaterbus.Application.Common.Interfaces;

/// <summary>
/// Trừu tượng hoá provider STT (nhận dạng giọng nói → text). Cùng khuôn với
/// <see cref="IChatCompletionService"/> và <see cref="ITextToSpeechService"/>.
/// </summary>
public interface ISpeechToTextService
{
    /// <summary>
    /// Trả về text khách vừa nói. Chuỗi RỖNG nghĩa là không nghe ra tiếng nói nào
    /// (khách bấm nhầm, mic tắt, toàn tiếng ồn) — gọi ở tầng trên phải xử lý case này
    /// chứ đừng đẩy chuỗi rỗng vào LLM.
    /// </summary>
    Task<string> TranscribeAsync(SpeechRecognitionRequest request, CancellationToken cancellationToken);
}

/// <param name="ContentType">
/// MIME của audio client gửi lên (audio/wav, audio/mp4, audio/webm...). Provider tự lo
/// việc định dạng nào nó nhận được.
/// </param>
/// <param name="Language">
/// Mã ISO đã chuẩn hoá ("vi"/"en"). Null = để provider tự phát hiện — an toàn hơn là ép sai.
/// </param>
/// <param name="Phrases">
/// Từ/cụm từ gợi ý cho bộ nhận dạng — tên ga, tên địa danh. Cần vì tên riêng là chỗ sai nhiều
/// nhất: đo thật thì "cầu Ba Son" bị chép thành "cậu Ba Son". Provider nào không hỗ trợ thì
/// bỏ qua, nên bên gọi cứ truyền.
/// </param>
public sealed record SpeechRecognitionRequest(
    byte[] Audio,
    string ContentType,
    string? Language = null,
    IReadOnlyList<string>? Phrases = null);
