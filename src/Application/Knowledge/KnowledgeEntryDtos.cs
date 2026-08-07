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

public sealed record KnowledgeEntryListDto(
    int TotalCount,
    int Page,
    int PageSize,
    IReadOnlyList<KnowledgeEntryDto> Items);

public sealed record KnowledgeEntryMetadataDto(
    IReadOnlyList<string> Categories,
    IReadOnlyList<string> Statuses,
    int MaxKeywords,
    int MaxKeywordLength,
    int MaxContentChars,
    int MaxTotalContentChars,
    int DefaultSearchTake,
    int MaxSearchTake);

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

public sealed record KnowledgeSearchTestResultDto(
    string Query,
    IReadOnlyList<string> Tokens,
    int TotalMatched,
    IReadOnlyList<KnowledgeSearchTestHitDto> Hits);

public sealed record KnowledgeSearchTestHitDto(
    Guid KnowledgeEntryId,
    string Title,
    string Category,
    IReadOnlyList<string> Keywords,
    int DisplayOrder,
    int Score,
    int MatchedTokens,
    bool HasStrongKeywordHit,
    string ContentSeenByAssistant);
