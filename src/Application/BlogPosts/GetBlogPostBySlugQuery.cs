using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;

namespace SaigonWaterbus.Application.BlogPosts;

public sealed record GetBlogPostBySlugQuery(string Slug) : IRequest<BlogPostDto>;

public sealed class GetBlogPostBySlugQueryValidator : AbstractValidator<GetBlogPostBySlugQuery>
{
    public GetBlogPostBySlugQueryValidator()
    {
        RuleFor(x => x.Slug).NotEmpty().MaximumLength(220);
    }
}

public sealed class GetBlogPostBySlugQueryHandler : IRequestHandler<GetBlogPostBySlugQuery, BlogPostDto>
{
    private readonly IApplicationDbContext _context;

    public GetBlogPostBySlugQueryHandler(IApplicationDbContext context) => _context = context;

    public async Task<BlogPostDto> Handle(GetBlogPostBySlugQuery request, CancellationToken cancellationToken)
    {
        var slug = BlogPostSupport.NormalizeSlug(request.Slug);

        var post = await _context.Set<BlogPost>()
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Slug == slug && x.Status == BlogPostSupport.PublishedStatus, cancellationToken)
            ?? throw new NotFoundException("Blog post not found.");

        return BlogPostSupport.ToDto(post);
    }
}
