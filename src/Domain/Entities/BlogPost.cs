using SaigonWaterbus.Domain.Common;

namespace SaigonWaterbus.Domain.Entities;

public class BlogPost : BaseGuidAuditableEntity
{
    public Guid AuthorId { get; set; }
    public string Title { get; set; } = null!;
    public string Slug { get; set; } = null!;
    public string? Summary { get; set; }
    public string Category { get; set; } = null!;
    public string? ImageUrl { get; set; }
    public string[] ImageUrls { get; set; } = [];
    public string? ImageAltText { get; set; }
    public string Content { get; set; } = null!;
    public string Status { get; set; } = "Draft";
    public DateTimeOffset? PublishedAt { get; set; }

    public User Author { get; set; } = null!;
}
