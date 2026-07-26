using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Application.BlogPosts;

public sealed record GetPublishedBlogPostListQuery : IRequest<IReadOnlyList<BlogPostSummaryDto>>;

public sealed class GetPublishedBlogPostListQueryHandler
    : IRequestHandler<GetPublishedBlogPostListQuery, IReadOnlyList<BlogPostSummaryDto>>
{
    private readonly IApplicationDbContext _context;

    public GetPublishedBlogPostListQueryHandler(IApplicationDbContext context) => _context = context;

    public async Task<IReadOnlyList<BlogPostSummaryDto>> Handle(
        GetPublishedBlogPostListQuery request,
        CancellationToken cancellationToken)
    {
        var posts = await _context.Set<BlogPost>()
            .AsNoTracking()
            .Where(x => x.Status == BlogPostSupport.PublishedStatus)
            .OrderByDescending(x => x.PublishedAt ?? x.Created)
            .ThenByDescending(x => x.Id)
            .ToListAsync(cancellationToken);

        return posts
            .Select(x => new BlogPostSummaryDto(
                x.Id,
                x.Title,
                x.Slug,
                x.Summary,
                x.Category,
                x.ImageUrl,
                BlogPostSupport.CreateImageUrls(x),
                x.ImageAltText,
                x.Status,
                x.PublishedAt,
                x.Created))
            .ToList();
    }
}
