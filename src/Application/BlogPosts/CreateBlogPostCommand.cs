using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Application.BlogPosts;

public sealed record CreateBlogPostCommand(
    string Title,
    string? Slug,
    string? Summary,
    string Content,
    string Category,
    string? Status,
    string? ImageUrl = null,
    string? ImageAltText = null,
    BlogPostImageFileRequest? ImageFile = null,
    IReadOnlyCollection<string>? ImageUrls = null,
    IReadOnlyCollection<BlogPostImageFileRequest>? ImageFiles = null) : IRequest<BlogPostDto>;

public sealed class CreateBlogPostCommandValidator : AbstractValidator<CreateBlogPostCommand>
{
    public CreateBlogPostCommandValidator()
    {
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
            .Must(status => string.IsNullOrWhiteSpace(status)
                || string.Equals(status, BlogPostSupport.DraftStatus, StringComparison.OrdinalIgnoreCase)
                || string.Equals(status, BlogPostSupport.PublishedStatus, StringComparison.OrdinalIgnoreCase))
            .WithMessage("Status khi tao moi hop le: Draft | Published.");
        RuleFor(x => x.Category)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .WithMessage("Category bat buoc nhap. Gia tri hop le: Activity | Event | News.")
            .Must(BlogPostSupport.IsValidCategory)
            .WithMessage("Category hop le: Activity | Event | News.");
    }
}

public sealed class CreateBlogPostCommandHandler : IRequestHandler<CreateBlogPostCommand, BlogPostDto>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;
    private readonly TimeProvider _timeProvider;
    private readonly IBlogImageStorageService? _blogImageStorage;

    public CreateBlogPostCommandHandler(
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

    public async Task<BlogPostDto> Handle(CreateBlogPostCommand request, CancellationToken cancellationToken)
    {
        BlogPostSupport.EnsureNoManualImageUrls(request.ImageUrl, request.ImageUrls);

        var actor = await BlogPostSupport.EnsureCurrentUserCanManageBlogPostsAsync(
            _context,
            _userContext,
            cancellationToken);

        var title = BlogPostSupport.NormalizeRequiredText(request.Title, nameof(request.Title), 200);
        var content = BlogPostSupport.NormalizeRequiredText(request.Content, nameof(request.Content));
        var status = string.IsNullOrWhiteSpace(request.Status)
            ? BlogPostSupport.DraftStatus
            : BlogPostSupport.NormalizeStatus(request.Status, nameof(request.Status));
        if (status == BlogPostSupport.ArchivedStatus)
        {
            throw new SaigonWaterbus.Application.Common.Exceptions.ValidationException(
                [new FluentValidation.Results.ValidationFailure(nameof(request.Status), "Status khi tao moi hop le: Draft | Published.")]);
        }

        var post = new BlogPost
        {
            AuthorId = actor.Id,
            Author = actor,
            Title = title,
            Slug = await BlogPostSupport.GenerateUniqueSlugAsync(
                _context,
                string.IsNullOrWhiteSpace(request.Slug) ? title : request.Slug,
                null,
                cancellationToken),
            Summary = BlogPostSupport.NormalizeOptionalText(request.Summary, nameof(request.Summary), 500),
            Category = BlogPostSupport.NormalizeCategory(request.Category, nameof(request.Category)),
            ImageUrl = null,
            ImageAltText = BlogPostSupport.NormalizeOptionalText(request.ImageAltText, nameof(request.ImageAltText), 200),
            Content = content,
            Status = status,
            PublishedAt = status == BlogPostSupport.PublishedStatus ? _timeProvider.GetUtcNow() : null
        };

        IReadOnlyCollection<string> imageUrls;
        if (request.ImageFile is not null)
        {
            imageUrls = [await BlogPostSupport.UploadImageAsync(
                post.Id,
                request.ImageFile,
                _blogImageStorage,
                nameof(request.ImageFile),
                cancellationToken)];
        }
        else if (request.ImageFiles is { Count: > 0 })
        {
            imageUrls = await BlogPostSupport.UploadImagesAsync(
                post.Id,
                request.ImageFiles,
                _blogImageStorage,
                nameof(request.ImageFiles),
                cancellationToken);
        }
        else
        {
            imageUrls = [];
        }

        BlogPostSupport.ApplyImageUrls(post, imageUrls);

        BlogPostSupport.EnsurePublishedPostHasImage(post, nameof(request.ImageUrl));

        _context.Set<BlogPost>().Add(post);
        await _context.SaveChangesAsync(cancellationToken);

        return BlogPostSupport.ToDto(post);
    }
}
