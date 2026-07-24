namespace SaigonWaterbus.Application.BlogPosts;

public sealed record BlogPostSummaryDto(
    Guid BlogPostId,
    string Title,
    string Slug,
    string? Summary,
    string Category,
    string? ImageUrl,
    IReadOnlyCollection<string> ImageUrls,
    string? ImageAltText,
    string Status,
    DateTimeOffset? PublishedAt,
    DateTimeOffset CreatedAt,
    Guid AuthorId,
    string AuthorName);

public sealed record BlogPostDto(
    Guid BlogPostId,
    Guid AuthorId,
    string AuthorName,
    string Title,
    string Slug,
    string? Summary,
    string Category,
    string? ImageUrl,
    IReadOnlyCollection<string> ImageUrls,
    string? ImageAltText,
    string Content,
    string ContentText,
    string ContentHtml,
    string Status,
    DateTimeOffset? PublishedAt,
    DateTimeOffset CreatedAt);
