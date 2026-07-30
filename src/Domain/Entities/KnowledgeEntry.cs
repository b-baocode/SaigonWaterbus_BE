using SaigonWaterbus.Domain.Common;

namespace SaigonWaterbus.Domain.Entities;

/// <summary>
/// Một mẩu kiến thức do Admin soạn để trợ lý ảo tra cứu: chính sách hoàn/huỷ vé, quy định
/// đi tàu, quy định hành lý, hướng dẫn đặt vé... Những thứ này KHÔNG suy ra được từ dữ liệu
/// vận hành (ga, chuyến, giá) nên trước đây trợ lý buộc phải trả lời "chưa có thông tin".
///
/// Mỗi dòng là MỘT chủ đề: <see cref="Title"/> là câu hỏi hoặc tiêu đề, <see cref="Content"/>
/// là câu trả lời. Chỉ entry <see cref="Status"/> = Published mới được trợ lý dùng.
/// </summary>
public class KnowledgeEntry : BaseGuidAuditableEntity
{
    public const string DraftStatus = "Draft";
    public const string PublishedStatus = "Published";

    public string Title { get; set; } = null!;

    public string Content { get; set; } = null!;

    /// <summary>Nhóm chủ đề, xem <see cref="Constants.KnowledgeCategories"/>.</summary>
    public string Category { get; set; } = null!;

    /// <summary>
    /// Từ khoá và các cách hỏi khác của cùng chủ đề (ví dụ "trả lại vé", "refund" cho chủ đề
    /// hoàn vé). Đây là thứ quyết định độ khớp khi tìm kiếm — tìm bằng khớp từ khoá nên không
    /// tự hiểu từ đồng nghĩa, phải khai ở đây.
    /// </summary>
    public string[] Keywords { get; set; } = [];

    public string Status { get; set; } = DraftStatus;

    /// <summary>Thứ tự ưu tiên khi nhiều entry cùng điểm khớp.</summary>
    public int DisplayOrder { get; set; }

    public Guid AuthorId { get; set; }

    /// <summary>
    /// Lần sửa gần nhất. Các entity khác khai updated_at bằng shadow property, nhưng ở đây admin
    /// cần thấy mốc này trên màn quản lý, mà <c>IApplicationDbContext</c> không expose
    /// <c>Entry()</c> để ghi shadow property — nên khai property thật thay vì nới interface dùng chung.
    /// </summary>
    public DateTimeOffset? UpdatedAt { get; set; }

    public User Author { get; set; } = null!;

    public static bool IsValidStatus(string? value) =>
        string.Equals(value, DraftStatus, StringComparison.OrdinalIgnoreCase)
        || string.Equals(value, PublishedStatus, StringComparison.OrdinalIgnoreCase);
}
