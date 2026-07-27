using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Application.BlogPosts;

public sealed record GetBlogPostManagementListQuery(string? Status) : IRequest<IReadOnlyList<BlogPostSummaryDto>>;

public sealed class GetBlogPostManagementListQueryValidator : AbstractValidator<GetBlogPostManagementListQuery>
{
    public GetBlogPostManagementListQueryValidator()
    {
        RuleFor(x => x.Status)
            .Must(status => string.IsNullOrWhiteSpace(status) || BlogPostSupport.IsValidStatus(status))
            .WithMessage("Status hop le: Draft | Published | Archived.");
    }
}

public sealed class GetBlogPostManagementListQueryHandler
    : IRequestHandler<GetBlogPostManagementListQuery, IReadOnlyList<BlogPostSummaryDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;

    public GetBlogPostManagementListQueryHandler(IApplicationDbContext context, IUserContext userContext)
    {
        _context = context;
        _userContext = userContext;
    }

    public async Task<IReadOnlyList<BlogPostSummaryDto>> Handle(
        GetBlogPostManagementListQuery request,
        CancellationToken cancellationToken)
    {
        await BlogPostSupport.EnsureCurrentUserCanManageBlogPostsAsync(_context, _userContext, cancellationToken);

        var query = _context.Set<BlogPost>().AsNoTracking();
        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            var status = BlogPostSupport.NormalizeStatus(request.Status, nameof(request.Status));
            query = query.Where(x => x.Status == status);
        }

        var posts = await query
            .OrderByDescending(x => x.Created)
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
