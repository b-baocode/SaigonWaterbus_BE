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
    string? ImageAltText,
    string Category,
    BlogPostImageFileRequest? ImageFile = null,
    IReadOnlyCollection<string>? ImageUrls = null,
    IReadOnlyCollection<BlogPostImageFileRequest>? ImageFiles = null) : IRequest<BlogPostDto>;

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
            .Must(string.IsNullOrWhiteSpace)
            .WithMessage(BlogPostSupport.UploadOnlyImageMessage);
        RuleFor(x => x.ImageUrls)
            .Must(x => x is null || x.All(string.IsNullOrWhiteSpace))
            .WithMessage(BlogPostSupport.UploadOnlyImageMessage);
        RuleFor(x => x.ImageAltText).MaximumLength(200);
        RuleFor(x => x.Content).NotEmpty();
        RuleFor(x => x.Status)
            .NotEmpty()
            .Must(BlogPostSupport.IsValidStatus)
            .WithMessage("Status hop le: Draft | Published | Archived.");
        RuleFor(x => x.Category)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .WithMessage("Category bat buoc nhap. Gia tri hop le: Activity | Event | News.")
            .Must(BlogPostSupport.IsValidCategory)
            .WithMessage("Category hop le: Activity | Event | News.");
    }
}

public sealed class UpdateBlogPostCommandHandler : IRequestHandler<UpdateBlogPostCommand, BlogPostDto>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;
    private readonly TimeProvider _timeProvider;
    private readonly IBlogImageStorageService? _blogImageStorage;

    public UpdateBlogPostCommandHandler(
        IApplicationDbContext context,
        IUserContext userContext,
        TimeProvider timeProvider,
        IBlogImageStorageService? blogImageStorage = null)
    {
        _context = context;
        _userContext = userContext;
        _timeProvider = timeProvider;
        _blogImageStorage = blogImageStorage;
    }

    public async Task<BlogPostDto> Handle(UpdateBlogPostCommand request, CancellationToken cancellationToken)
    {
        BlogPostSupport.EnsureNoManualImageUrls(request.ImageUrl, request.ImageUrls);

        await BlogPostSupport.EnsureCurrentUserCanManageBlogPostsAsync(_context, _userContext, cancellationToken);

        var post = await _context.Set<BlogPost>()
            .Include(x => x.Author)
            .SingleOrDefaultAsync(x => x.Id == request.BlogPostId, cancellationToken)
            ?? throw new NotFoundException("Blog post not found.");

        var title = BlogPostSupport.NormalizeRequiredText(request.Title, nameof(request.Title), 200);
        var content = BlogPostSupport.NormalizeRequiredText(request.Content, nameof(request.Content));
        var status = BlogPostSupport.NormalizeStatus(request.Status, nameof(request.Status));
        var category = BlogPostSupport.NormalizeCategory(request.Category, nameof(request.Category));

        post.Title = title;
        post.Slug = await BlogPostSupport.GenerateUniqueSlugAsync(
            _context,
            string.IsNullOrWhiteSpace(request.Slug) ? title : request.Slug,
            post.Id,
            cancellationToken);
        post.Summary = BlogPostSupport.NormalizeOptionalText(request.Summary, nameof(request.Summary), 500);
        post.Category = category;
        if (request.ImageFile is not null)
        {
            var imageUrl = await BlogPostSupport.UploadImageAsync(
                post.Id,
                request.ImageFile,
                _blogImageStorage,
                nameof(request.ImageFile),
                cancellationToken);
            BlogPostSupport.ApplyImageUrls(post, [imageUrl]);
        }
        else if (request.ImageFiles is { Count: > 0 })
        {
            var imageUrls = await BlogPostSupport.UploadImagesAsync(
                post.Id,
                request.ImageFiles,
                _blogImageStorage,
                nameof(request.ImageFiles),
                cancellationToken);
            BlogPostSupport.ApplyImageUrls(post, imageUrls);
        }
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
