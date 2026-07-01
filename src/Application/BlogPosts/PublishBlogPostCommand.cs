using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;

namespace SaigonWaterbus.Application.BlogPosts;

public sealed record PublishBlogPostCommand(Guid BlogPostId) : IRequest<BlogPostDto>;

public sealed class PublishBlogPostCommandValidator : AbstractValidator<PublishBlogPostCommand>
{
    public PublishBlogPostCommandValidator()
    {
        RuleFor(x => x.BlogPostId).NotEmpty();
    }
}

public sealed class PublishBlogPostCommandHandler : IRequestHandler<PublishBlogPostCommand, BlogPostDto>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;
    private readonly TimeProvider _timeProvider;

    public PublishBlogPostCommandHandler(
        IApplicationDbContext context,
        IUserContext userContext,
        TimeProvider timeProvider)
    {
        _context = context;
        _userContext = userContext;
        _timeProvider = timeProvider;
    }

    public async Task<BlogPostDto> Handle(PublishBlogPostCommand request, CancellationToken cancellationToken)
    {
        await BlogPostSupport.EnsureCurrentUserCanManageBlogPostsAsync(_context, _userContext, cancellationToken);

        var post = await _context.Set<BlogPost>()
            .Include(x => x.Author)
            .SingleOrDefaultAsync(x => x.Id == request.BlogPostId, cancellationToken)
            ?? throw new NotFoundException("Blog post not found.");

        post.Status = BlogPostSupport.PublishedStatus;
        BlogPostSupport.EnsurePublishedPostHasImage(post, nameof(post.ImageUrl));
        post.PublishedAt ??= _timeProvider.GetUtcNow();

        await _context.SaveChangesAsync(cancellationToken);

        return BlogPostSupport.ToDto(post);
    }
}
