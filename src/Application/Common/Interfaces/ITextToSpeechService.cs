namespace SaigonWaterbus.Application.Common.Interfaces;

/// <summary>
/// Trừu tượng hoá provider TTS (Google Cloud TTS, VieNeu-TTS tự host, ...). Cùng khuôn với
/// <see cref="IChatCompletionService"/>: tầng Application chỉ biết interface này, đổi provider
/// = viết class mới trong Infrastructure, không đụng logic hướng dẫn viên.
///
/// Có interface này vì v1 dùng cloud TTS cho nhanh, nhưng đích nhắm là quay lại VieNeu-TTS
/// tự host để giọng hỏi-đáp KHỚP với giọng đã pre-bake cho landmark (xem
/// CSDocument/tour-guide-voice-plan.md).
/// </summary>
public interface ITextToSpeechService
{
    Task<SpeechAudio> SynthesizeAsync(SpeechSynthesisRequest request, CancellationToken cancellationToken);
}

/// <param name="Voice">
/// Tên giọng theo cách gọi của provider. Bỏ trống thì provider tự chọn giọng mặc định
/// ứng với <paramref name="Language"/> — Application không nên biết tên giọng cụ thể.
/// </param>
/// <param name="Language">Mã ISO đã chuẩn hoá ("vi"/"en"). Null = để provider dùng mặc định.</param>
public sealed record SpeechSynthesisRequest(string Text, string? Voice = null, string? Language = null);

/// <summary>Audio đã sinh, kèm content type để Web trả thẳng ra response.</summary>
public sealed record SpeechAudio(byte[] Data, string ContentType);
