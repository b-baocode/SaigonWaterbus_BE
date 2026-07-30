using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Application.Knowledge;

/// <summary>Dùng chung giữa các command/query admin: mapping DTO và làm sạch input.</summary>
public static class KnowledgeEntrySupport
{
    public const int MaxKeywords = 30;
    public const int MaxKeywordLength = 100;

    public static KnowledgeEntryDto ToDto(KnowledgeEntry entry) =>
        new(entry.Id, entry.Title, entry.Content, entry.Category, entry.Keywords,
            entry.Status, entry.DisplayOrder, entry.AuthorId, entry.Created, entry.UpdatedAt);

    /// <summary>
    /// Bỏ từ khoá rỗng, cắt khoảng trắng, loại trùng (không phân biệt hoa/thường). Từ khoá rác
    /// làm loãng điểm khớp nên lọc ngay ở cửa vào thay vì lúc tìm kiếm.
    /// </summary>
    public static string[] SanitizeKeywords(IEnumerable<string>? keywords) =>
        (keywords ?? [])
        .Where(k => !string.IsNullOrWhiteSpace(k))
        .Select(k => k.Trim())
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    /// <summary>Không truyền status thì mặc định Draft — nội dung phải được duyệt mới ra tới khách.</summary>
    public static string ResolveStatus(string? status) =>
        string.IsNullOrWhiteSpace(status)
            ? KnowledgeEntry.DraftStatus
            : (string.Equals(status, KnowledgeEntry.PublishedStatus, StringComparison.OrdinalIgnoreCase)
                ? KnowledgeEntry.PublishedStatus
                : KnowledgeEntry.DraftStatus);

    public static string ResolveCategory(string? category) =>
        KnowledgeCategories.Canonicalize(category) ?? KnowledgeCategories.Other;

    /// <summary>Dùng trong validator của cả create và update để hai đường ràng buộc y hệt nhau.</summary>
    public static bool IsKeywordCountValid(IReadOnlyCollection<string>? keywords) =>
        keywords is null || keywords.Count <= MaxKeywords;

    public static bool IsKeywordLengthValid(IReadOnlyCollection<string>? keywords) =>
        keywords is null || keywords.All(k => k is null || k.Length <= MaxKeywordLength);
}
