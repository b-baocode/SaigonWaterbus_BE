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
/// Bản rút gọn trả cho trợ lý ảo. Cố ý KHÔNG mang id/status/author — model không cần, và
/// mọi field thừa đều là token phải trả tiền ở mỗi lượt gọi LLM.
/// </summary>
public sealed record KnowledgeSearchHitDto(
    string Title,
    string Category,
    string Content);
