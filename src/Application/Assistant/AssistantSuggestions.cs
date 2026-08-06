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
                    [new("View ticket policies", "/terms-and-policy"), new("Book a ticket", "/waterbus-booking")])
                : (["Tôi đặt vé Waterbus như thế nào?", "Waterbus hỗ trợ thanh toán bằng cách nào?"],
                    [new("Xem chính sách vé", "/terms-and-policy"), new("Đặt vé", "/waterbus-booking")]);
        }

        if (ContainsAny(q, "dat ve", "dat ve", "booking", "book", "ticket"))
        {
            return english
                ? (["What trips are available today?", "Can I change or cancel my ticket?"],
                    [new("View schedule", "/schedule"), new("View ticket policies", "/terms-and-policy")])
                : (["Hôm nay có những chuyến nào?", "Tôi có thể đổi hoặc hủy vé không?"],
                    [new("Xem lịch trình", "/schedule"), new("Xem chính sách vé", "/terms-and-policy")]);
        }

        if (ContainsAny(q, "lich", "chuyen", "gio", "ga", "schedule", "station", "trip"))
        {
            return english
                ? (["How much is a Waterbus ticket?", "How do I book a ticket?"],
                    [new("Book a ticket", "/waterbus-booking"), new("View all schedules", "/schedule")])
                : (["Giá vé Waterbus là bao nhiêu?", "Tôi đặt vé như thế nào?"],
                    [new("Đặt vé", "/waterbus-booking"), new("Xem toàn bộ lịch trình", "/schedule")]);
        }

        if (ContainsAny(q, "thanh toan", "payment", "pay", "khong nhan", "chua nhan"))
        {
            return english
                ? (["How do I book a Waterbus ticket?", "Can I cancel or refund my ticket?"],
                    [new("Book a ticket", "/waterbus-booking"), new("View ticket policies", "/terms-and-policy")])
                : (["Tôi đặt vé Waterbus như thế nào?", "Tôi có thể hủy hoặc hoàn vé không?"],
                    [new("Đặt vé", "/waterbus-booking"), new("Xem chính sách vé", "/terms-and-policy")]);
        }

        return english
            ? (["What trips are available today?", "How much is a Waterbus ticket?"],
                [new("Book a ticket", "/waterbus-booking"), new("View schedule", "/schedule"), new("Contact support", "/contact")])
            : (["Hôm nay có những chuyến nào?", "Giá vé Waterbus là bao nhiêu?"],
                [new("Đặt vé", "/waterbus-booking"), new("Xem lịch trình", "/schedule"), new("Liên hệ hỗ trợ", "/contact")]);
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
