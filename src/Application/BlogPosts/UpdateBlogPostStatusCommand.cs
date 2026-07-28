using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;

namespace SaigonWaterbus.Application.BlogPosts;

public sealed record UpdateBlogPostStatusCommand(Guid BlogPostId, string Status) : IRequest<BlogPostDto>;

public sealed class UpdateBlogPostStatusCommandValidator : AbstractValidator<UpdateBlogPostStatusCommand>
{
    public UpdateBlogPostStatusCommandValidator()
    {
        RuleFor(x => x.BlogPostId).NotEmpty();
        RuleFor(x => x.Status)
            .NotEmpty()
            .Must(BlogPostSupport.IsValidStatus)
            .WithMessage("Status hop le: Draft | Published.");
    }
}

public sealed class UpdateBlogPostStatusCommandHandler
    : IRequestHandler<UpdateBlogPostStatusCommand, BlogPostDto>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;
    private readonly TimeProvider _timeProvider;

    public UpdateBlogPostStatusCommandHandler(
        IApplicationDbContext context,
        IUserContext userContext,
        TimeProvider timeProvider)
    {
        _context = context;
        _userContext = userContext;
        _timeProvider = timeProvider;
    }

    public async Task<BlogPostDto> Handle(
        UpdateBlogPostStatusCommand request,
        CancellationToken cancellationToken)
    {
        await BlogPostSupport.EnsureCurrentUserCanManageBlogPostsAsync(_context, _userContext, cancellationToken);

        var post = await _context.Set<BlogPost>()
            .SingleOrDefaultAsync(x => x.Id == request.BlogPostId, cancellationToken)
            ?? throw new NotFoundException("Blog post not found.");

        var status = BlogPostSupport.NormalizeStatus(request.Status, nameof(request.Status));
        post.Status = status;
        if (status == BlogPostSupport.PublishedStatus)
        {
            BlogPostSupport.EnsurePublishedPostHasImage(post, nameof(post.ImageUrl));
            post.PublishedAt ??= _timeProvider.GetUtcNow();
        }
        else
        {
            post.PublishedAt = null;
        }

        await _context.SaveChangesAsync(cancellationToken);

        return BlogPostSupport.ToDto(post);
    }
}
