namespace SaigonWaterbus.Application.Assistant;

/// <summary>
/// Ngôn ngữ trả lời của trợ lý, do FE gửi lên theo toggle VN/ENG của khung chat.
///
/// Nhận khoan dung nhiều dạng mã vì FE hiện dùng "VN"/"ENG" (xem AIChatbotPanel), còn client
/// khác có thể gửi mã ISO "vi"/"en"/"en-US". Không nhận diện được thì trả null, và lúc đó
/// prompt để model tự bám theo ngôn ngữ khách đang dùng — an toàn hơn là ép sai ngôn ngữ.
/// </summary>
public static class AssistantLanguage
{
    public const string Vietnamese = "vi";
    public const string English = "en";

    /// <summary>Chuẩn hoá mã ngôn ngữ client gửi lên. Không nhận ra thì null = tự phát hiện.</summary>
    public static string? Resolve(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim().ToLowerInvariant().Replace('_', '-');

        // Cắt phần vùng: "en-US" -> "en", "vi-VN" -> "vi".
        var primary = normalized.Split('-')[0];

        return primary switch
        {
            "vi" or "vn" or "vie" or "vietnamese" or "tiengviet" => Vietnamese,
            "en" or "eng" or "english" => English,
            _ => null,
        };
    }

    /// <summary>
    /// Client mở khung chat bằng một tin nhắn mồi: "start" = khách đang xem giao diện tiếng Anh,
    /// "bắt đầu" = tiếng Việt. Trả về mã ngôn ngữ tương ứng, hoặc null nếu đây là câu hỏi thật.
    ///
    /// CHỈ dùng cho LƯỢT ĐẦU của hội thoại và KHÔNG lưu lại: lời chào phải đúng ngôn ngữ giao diện,
    /// nhưng từ lượt sau khách gõ tiếng gì thì trả lời tiếng đó — khách bấm nút tiếng Anh rồi hỏi
    /// bằng tiếng Việt mà bị đáp tiếng Anh là khó chịu.
    ///
    /// So khớp sau khi bỏ dấu nên "bắt đầu", "Bat dau", "BẮT ĐẦU" đều nhận; chấm/than cuối câu
    /// cũng bỏ qua vì client hay thêm.
    /// </summary>
    public static string? ResolveGreetingTrigger(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var normalized = new string(text
                .Normalize(System.Text.NormalizationForm.FormD)
                .Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c)
                    != System.Globalization.UnicodeCategory.NonSpacingMark)
                .ToArray())
            .Replace('đ', 'd').Replace('Đ', 'D')
            .Trim()
            .Trim('.', '!', '/')
            .ToLowerInvariant();

        return normalized switch
        {
            "start" => English,
            "bat dau" => Vietnamese,
            _ => null,
        };
    }

    /// <summary>Câu chỉ dẫn ngôn ngữ nhét vào system prompt.</summary>
    public static string PromptInstruction(string? resolvedLanguage) => resolvedLanguage switch
    {
        Vietnamese => "Trả lời bằng TIẾNG VIỆT.",
        English => "Trả lời bằng TIẾNG ANH (English) — toàn bộ câu trả lời, kể cả câu từ chối "
                 + "và câu \"chưa có thông tin\". Tên ga/bến và tên tuyến thì giữ nguyên tiếng Việt "
                 + "vì đó là tên riêng trong hệ thống (ví dụ \"Bến Bạch Đằng\").",
        _ => "Trả lời bằng ĐÚNG NGÔN NGỮ khách đang dùng ở tin nhắn gần nhất: khách viết tiếng Việt "
           + "thì trả lời tiếng Việt, khách viết tiếng Anh thì trả lời tiếng Anh. Tên ga/bến và tên "
           + "tuyến luôn giữ nguyên tiếng Việt vì đó là tên riêng trong hệ thống.",
    };
}
