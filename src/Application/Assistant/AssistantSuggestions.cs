namespace SaigonWaterbus.Application.Assistant;

public sealed record AssistantAction(string Label, string Route, string Type = "navigate");

public static class AssistantSuggestions
{
    public static (IReadOnlyList<string> Questions, IReadOnlyList<AssistantAction> Actions) Build(
        string question,
        string? language)
    {
        var english = string.Equals(AssistantLanguage.Resolve(language), AssistantLanguage.English, StringComparison.OrdinalIgnoreCase);
        var q = Normalize(question);

        if (ContainsAny(q, "hoan", "huy", "doi", "refund", "cancel", "change"))
        {
            return english
                ? (["How do I book a Waterbus ticket?", "What payment methods are supported?"],
                    [new("View ticket policies", "/terms-and-policy"), new("Book a ticket in chat", "/waterbus-booking", "booking")])
                : (["Tôi đặt vé Waterbus như thế nào?", "Waterbus hỗ trợ thanh toán bằng cách nào?"],
                    [new("Xem chính sách vé", "/terms-and-policy"), new("Đặt vé trong chat", "/waterbus-booking", "booking")]);
        }

        if (ContainsAny(q, "dat ve", "dat ve", "booking", "book", "ticket"))
        {
            return english
                ? (["What trips are available today?", "Can I change or cancel my ticket?"],
                    [new("Book a ticket in chat", "/waterbus-booking", "booking"), new("View schedule", "/schedule")])
                : (["Hôm nay có những chuyến nào?", "Tôi có thể đổi hoặc hủy vé không?"],
                    [new("Đặt vé trong chat", "/waterbus-booking", "booking"), new("Xem lịch trình", "/schedule")]);
        }

        if (ContainsAny(q, "lich", "chuyen", "gio", "ga", "schedule", "station", "trip"))
        {
            return english
                ? (["How much is a Waterbus ticket?", "How do I book a ticket?"],
                    [new("View all schedules", "/schedule")])
                : (["Giá vé Waterbus là bao nhiêu?", "Tôi đặt vé như thế nào?"],
                    [new("Xem toàn bộ lịch trình", "/schedule")]);
        }

        if (ContainsAny(q, "thanh toan", "payment", "pay", "khong nhan", "chua nhan"))
        {
            return english
                ? (["How do I book a Waterbus ticket?", "Can I cancel or refund my ticket?"],
                    [new("Book a ticket in chat", "/waterbus-booking", "booking")])
                : (["Tôi đặt vé Waterbus như thế nào?", "Tôi có thể hủy hoặc hoàn vé không?"],
                    [new("Đặt vé trong chat", "/waterbus-booking", "booking")]);
        }

        // Không hiển thị nút điều hướng cho lời chào hoặc câu hỏi ngoài nhóm
        // chức năng. Nút chỉ xuất hiện khi nội dung user thực sự đề cập tới
        // đặt vé, lịch trình, chính sách hoặc thanh toán.
        return english
            ? (["What trips are available today?", "How much is a Waterbus ticket?"],
                [])
            : (["Hôm nay có những chuyến nào?", "Giá vé Waterbus là bao nhiêu?"],
                []);
    }

    private static string Normalize(string value)
    {
        var normalized = value.Normalize(System.Text.NormalizationForm.FormD);
        return new string(normalized.Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark).ToArray())
            .Replace('đ', 'd').Replace('Đ', 'D').ToLowerInvariant();
    }

    private static bool ContainsAny(string value, params string[] terms) =>
        terms.Any(value.Contains);
}
