namespace SaigonWaterbus.Application.Knowledge;

/// <summary>Bản đầy đủ cho màn quản lý của Admin.</summary>
public sealed record KnowledgeEntryDto(
    Guid KnowledgeEntryId,
    string Title,
    string Content,
    string Category,
    IReadOnlyList<string> Keywords,
    string Status,
    int DisplayOrder,
    Guid AuthorId,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

/// <summary>
/// Bản công khai cho khách xem trên web. KHÔNG lộ status (khách chỉ thấy Published),
/// author (thông tin nội bộ), và keywords (từ khoá phục vụ tìm kiếm của trợ lý, hiện ra
/// chỉ làm rối). Giữ UpdatedAt để FE hiển thị "cập nhật lần cuối" cho điều khoản.
/// </summary>
public sealed record PublicKnowledgeEntryDto(
    Guid KnowledgeEntryId,
    string Title,
    string Content,
    string Category,
    int DisplayOrder,
    DateTimeOffset? UpdatedAt);

/// <summary>
/// Bản rút gọn trả cho trợ lý ảo. Cố ý KHÔNG mang id/status/author — model không cần, và
/// mọi field thừa đều là token phải trả tiền ở mỗi lượt gọi LLM.
/// </summary>
public sealed record KnowledgeSearchHitDto(
    string Title,
    string Category,
    string Content);
