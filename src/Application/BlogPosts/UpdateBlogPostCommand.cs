using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;

namespace SaigonWaterbus.Application.BlogPosts;

public sealed record UpdateBlogPostCommand(
    Guid BlogPostId,
    string Title,
    string? Slug,
    string? Summary,
    string Content,
    string Status,
    string? ImageUrl,
    string? ImageAltText) : IRequest<BlogPostDto>;

public sealed class UpdateBlogPostCommandValidator : AbstractValidator<UpdateBlogPostCommand>
{
    public UpdateBlogPostCommandValidator()
    {
        RuleFor(x => x.BlogPostId).NotEmpty();
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Slug).MaximumLength(220);
        RuleFor(x => x.Summary).MaximumLength(500);
        RuleFor(x => x.ImageUrl)
            .MaximumLength(2048)
            .Must(x => string.IsNullOrWhiteSpace(x) || Uri.TryCreate(x, UriKind.Absolute, out _))
            .WithMessage("ImageUrl must be an absolute URL.");
        RuleFor(x => x.ImageAltText).MaximumLength(200);
        RuleFor(x => x.Content).NotEmpty();
        RuleFor(x => x.Status)
            .NotEmpty()
            .Must(BlogPostSupport.IsValidStatus)
            .WithMessage("Status hop le: Draft | Published | Archived.");
    }
}

public sealed class UpdateBlogPostCommandHandler : IRequestHandler<UpdateBlogPostCommand, BlogPostDto>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;
    private readonly TimeProvider _timeProvider;

    public UpdateBlogPostCommandHandler(
        IApplicationDbContext context,
        IUserContext userContext,
        TimeProvider timeProvider)
    {
        _context = context;
        _userContext = userContext;
        _timeProvider = timeProvider;
    }

    public async Task<BlogPostDto> Handle(UpdateBlogPostCommand request, CancellationToken cancellationToken)
    {
        await BlogPostSupport.EnsureCurrentUserCanManageBlogPostsAsync(_context, _userContext, cancellationToken);

        var post = await _context.Set<BlogPost>()
            .Include(x => x.Author)
            .SingleOrDefaultAsync(x => x.Id == request.BlogPostId, cancellationToken)
            ?? throw new NotFoundException("Blog post not found.");

        var title = BlogPostSupport.NormalizeRequiredText(request.Title, nameof(request.Title), 200);
        var content = BlogPostSupport.NormalizeRequiredText(request.Content, nameof(request.Content));
        var status = BlogPostSupport.NormalizeStatus(request.Status, nameof(request.Status));
        var imageUrl = BlogPostSupport.NormalizeImageUrl(request.ImageUrl, nameof(request.ImageUrl));

        post.Title = title;
        post.Slug = await BlogPostSupport.GenerateUniqueSlugAsync(
            _context,
            request.Slug ?? title,
            post.Id,
            cancellationToken);
        post.Summary = BlogPostSupport.NormalizeOptionalText(request.Summary, nameof(request.Summary), 500);
        post.ImageUrl = imageUrl;
        post.ImageAltText = BlogPostSupport.NormalizeOptionalText(request.ImageAltText, nameof(request.ImageAltText), 200);
        post.Content = content;
        post.Status = status;
        BlogPostSupport.EnsurePublishedPostHasImage(post, nameof(request.ImageUrl));

        if (status == BlogPostSupport.PublishedStatus && !post.PublishedAt.HasValue)
        {
            post.PublishedAt = _timeProvider.GetUtcNow();
        }
        else if (status == BlogPostSupport.DraftStatus)
        {
            post.PublishedAt = null;
        }

        await _context.SaveChangesAsync(cancellationToken);

        return BlogPostSupport.ToDto(post);
    }
}
