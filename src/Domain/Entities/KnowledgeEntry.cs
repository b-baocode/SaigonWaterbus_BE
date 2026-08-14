using SaigonWaterbus.Domain.Common;

namespace SaigonWaterbus.Domain.Entities;

/// <summary>
/// Một mẩu kiến thức do Admin soạn để trợ lý ảo tra cứu: chính sách hoàn/huỷ vé, quy định
/// đi tàu, quy định hành lý, hướng dẫn đặt vé... Những thứ này KHÔNG suy ra được từ dữ liệu
/// vận hành (ga, chuyến, giá) nên trước đây trợ lý buộc phải trả lời "chưa có thông tin".
///
/// Mỗi dòng là MỘT chủ đề: <see cref="Title"/> là câu hỏi hoặc tiêu đề, <see cref="Content"/>
/// là câu trả lời. Ai được đọc thì xem <see cref="Status"/>.
/// </summary>
public class KnowledgeEntry : BaseGuidAuditableEntity
{
    /// <summary>Đang soạn: KHÔNG ai dùng — không hiện cho khách, trợ lý cũng không đọc.</summary>
    public const string DraftStatus = "Draft";

    /// <summary>Kiến thức nội bộ: CHỈ trợ lý đọc, không hiện trên web.</summary>
    public const string PrivateStatus = "Private";

    /// <summary>Chính sách/quy định công khai: hiện trên web VÀ trợ lý đọc được.</summary>
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

    /// <summary>
    /// Ba trạng thái hợp lệ, xếp theo mức "mở dần" để hiện lên UI quản trị.
    ///
    /// CỐ Ý GIỮ TÊN "Published" thay vì đổi thành "Public" cho đối xứng với "Private": đổi tên là
    /// phải sửa dữ liệu đang có bằng migration, mà việc thêm Private thì tự nó không cần đụng DB —
    /// cột status chỉ là varchar, không có check constraint.
    /// </summary>
    public static readonly string[] AllStatuses = [DraftStatus, PrivateStatus, PublishedStatus];

    public static bool IsValidStatus(string? value) =>
        AllStatuses.Contains(value, StringComparer.OrdinalIgnoreCase);
}
