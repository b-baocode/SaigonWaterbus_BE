using System.Buffers.Binary;

namespace SaigonWaterbus.Application.TourGuide;

/// <summary>
/// Phát hiện audio im lặng TRƯỚC khi gọi STT.
///
/// VÌ SAO CẦN: đã đo thật — gửi 2 giây im lặng tuyệt đối cho Gemini 3 lần thì 2 lần nó BỊA ra
/// câu hoàn toàn không có thật ("cho tôi biết kết quả xổ số kiến thiết miền Bắc hôm nay"), chỉ
/// 1 lần báo đúng là không nghe được. Nhắc trong prompt không đủ — model sinh chữ giỏi hơn là
/// im lặng. Mà khách bấm nhầm nút ghi âm là chuyện xảy ra suốt.
///
/// Đo biên độ là cách chặn DUY NHẤT đáng tin: nó tất định, tốn vài mili giây, và tiết kiệm luôn
/// một lần gọi STT + một lần gọi LLM cho mỗi cú bấm nhầm.
///
/// Chỉ đọc được WAV PCM 16-bit — đúng định dạng FE bắt buộc phải gửi (xem
/// CSDocument/tour-guide-voice-plan.md). Định dạng khác thì trả false: không chắc thì để STT
/// quyết, thà tốn một lần gọi còn hơn nuốt mất câu hỏi thật của khách.
/// </summary>
public static class SilentAudioDetector
{
    /// <summary>
    /// Ngưỡng RMS trên thang int16 (0–32767). Đặt RẤT thấp (~0.15% toàn thang) để chỉ bắt đúng
    /// trường hợp gần như im lặng số: mic bị tắt, chưa cấp quyền, bấm nhầm rồi thả ngay.
    /// Tiếng nói thật dù thu ở xa cũng cho RMS cao hơn nhiều, kể cả khi có tiếng máy tàu.
    /// </summary>
    private const double SilenceRmsThreshold = 50;

    /// <summary>Ngắn hơn mức này thì không thể là một câu hỏi.</summary>
    private const double MinimumSeconds = 0.3;

    public static bool IsSilent(byte[] audio, string? contentType)
    {
        var bare = (contentType ?? string.Empty).Split(';')[0].Trim();
        if (!bare.Equals("audio/wav", StringComparison.OrdinalIgnoreCase)
            && !bare.Equals("audio/wave", StringComparison.OrdinalIgnoreCase)
            && !bare.Equals("audio/x-wav", StringComparison.OrdinalIgnoreCase)
            && !bare.Equals("audio/vnd.wave", StringComparison.OrdinalIgnoreCase)
            && !bare.Equals("audio/vnd.wav", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!TryReadPcm16(audio, out var samples, out var sampleRate) || samples.Length == 0)
        {
            return false;
        }

        if (samples.Length / (double)Math.Max(1, sampleRate) < MinimumSeconds)
        {
            return true;
        }

        // Cộng bình phương trên double: tổng của cả trăm nghìn mẫu int16 tràn long rất nhanh.
        var sumOfSquares = 0d;
        foreach (var sample in samples)
        {
            sumOfSquares += (double)sample * sample;
        }

        return Math.Sqrt(sumOfSquares / samples.Length) < SilenceRmsThreshold;
    }

    /// <summary>
    /// Đọc mẫu PCM 16-bit từ WAV. Duyệt từng chunk chứ không giả định "data" nằm ở offset 44 —
    /// nhiều bộ ghi âm chèn thêm chunk LIST/fact trước data.
    /// </summary>
    private static bool TryReadPcm16(byte[] audio, out short[] samples, out int sampleRate)
    {
        samples = [];
        sampleRate = 0;

        var span = audio.AsSpan();
        if (span.Length < 12
            || !span[..4].SequenceEqual("RIFF"u8)
            || !span.Slice(8, 4).SequenceEqual("WAVE"u8))
        {
            return false;
        }

        var offset = 12;
        var bitsPerSample = 0;
        var isPcm = false;

        while (offset + 8 <= span.Length)
        {
            var chunkId = span.Slice(offset, 4);
            var chunkSize = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(offset + 4, 4));
            var body = offset + 8;

            if (chunkSize < 0 || body + chunkSize > span.Length)
            {
                // Chunk cuối đôi khi khai độ dài lớn hơn dữ liệu thật (file bị cắt giữa chừng):
                // lấy phần còn lại thay vì bỏ cuộc.
                chunkSize = span.Length - body;
                if (chunkSize <= 0)
                {
                    break;
                }
            }

            if (chunkId.SequenceEqual("fmt "u8) && chunkSize >= 16)
            {
                isPcm = BinaryPrimitives.ReadInt16LittleEndian(span.Slice(body, 2)) == 1;
                sampleRate = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(body + 4, 4));
                bitsPerSample = BinaryPrimitives.ReadInt16LittleEndian(span.Slice(body + 14, 2));
            }
            else if (chunkId.SequenceEqual("data"u8))
            {
                if (!isPcm || bitsPerSample != 16)
                {
                    return false;
                }

                var count = chunkSize / 2;
                var result = new short[count];
                for (var i = 0; i < count; i++)
                {
                    result[i] = BinaryPrimitives.ReadInt16LittleEndian(span.Slice(body + (i * 2), 2));
                }

                samples = result;
                return sampleRate > 0;
            }

            // Chunk luôn căn lề 2 byte.
            offset = body + chunkSize + (chunkSize % 2);
        }

        return false;
    }
}
