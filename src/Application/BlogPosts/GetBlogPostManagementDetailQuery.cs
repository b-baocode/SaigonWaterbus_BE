using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;

namespace SaigonWaterbus.Application.BlogPosts;

public sealed record GetBlogPostManagementDetailQuery(Guid BlogPostId) : IRequest<BlogPostDto>;

public sealed class GetBlogPostManagementDetailQueryValidator : AbstractValidator<GetBlogPostManagementDetailQuery>
{
    public GetBlogPostManagementDetailQueryValidator()
    {
        RuleFor(x => x.BlogPostId).NotEmpty();
    }
}

public sealed class GetBlogPostManagementDetailQueryHandler : IRequestHandler<GetBlogPostManagementDetailQuery, BlogPostDto>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;

    public GetBlogPostManagementDetailQueryHandler(IApplicationDbContext context, IUserContext userContext)
    {
        _context = context;
        _userContext = userContext;
    }

    public async Task<BlogPostDto> Handle(GetBlogPostManagementDetailQuery request, CancellationToken cancellationToken)
    {
        await BlogPostSupport.EnsureCurrentUserCanManageBlogPostsAsync(_context, _userContext, cancellationToken);

        var post = await _context.Set<BlogPost>()
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == request.BlogPostId, cancellationToken)
            ?? throw new NotFoundException("Blog post not found.");

        return BlogPostSupport.ToDto(post);
    }
}
