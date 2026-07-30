namespace SaigonWaterbus.Domain.Constants;

/// <summary>Nhóm chủ đề của <see cref="Entities.KnowledgeEntry"/>. Dùng để admin lọc khi quản lý.</summary>
public static class KnowledgeCategories
{
    /// <summary>Hoàn vé, huỷ vé, đổi vé.</summary>
    public const string Refund = "Refund";

    /// <summary>Quy định hành lý, đồ mang theo.</summary>
    public const string Luggage = "Luggage";

    /// <summary>Quy định đi tàu, an toàn, trẻ em, vật nuôi.</summary>
    public const string Rules = "Rules";

    /// <summary>Hướng dẫn đặt vé, check-in, xuất vé.</summary>
    public const string Booking = "Booking";

    /// <summary>Thanh toán, hoá đơn, phương thức trả tiền.</summary>
    public const string Payment = "Payment";

    /// <summary>Dịch vụ trên tàu, tiện ích, hỗ trợ khách.</summary>
    public const string Service = "Service";

    public const string Other = "Other";

    public static readonly string[] All =
        [Refund, Luggage, Rules, Booking, Payment, Service, Other];

    public static bool IsValid(string? value) =>
        value is not null
        && All.Any(x => string.Equals(x, value, StringComparison.OrdinalIgnoreCase));

    /// <summary>Chuẩn hoá về đúng chữ hoa/thường của hằng để lưu DB nhất quán.</summary>
    public static string? Canonicalize(string? value) =>
        All.FirstOrDefault(x => string.Equals(x, value, StringComparison.OrdinalIgnoreCase));
}
