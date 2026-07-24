using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;

namespace SaigonWaterbus.Application.BlogPosts;

public sealed record UpdateBlogPostImageCommand(
    Guid BlogPostId,
    string? ImageUrl,
    string? ImageAltText = null,
    BlogPostImageFileRequest? ImageFile = null,
    IReadOnlyCollection<string>? ImageUrls = null,
    IReadOnlyCollection<BlogPostImageFileRequest>? ImageFiles = null) : IRequest<BlogPostDto>;

public sealed record BlogPostImageFileRequest(
    string FileName,
    string? ContentType,
    long Length,
    Stream Content);

public sealed class UpdateBlogPostImageCommandValidator : AbstractValidator<UpdateBlogPostImageCommand>
{
    public UpdateBlogPostImageCommandValidator()
    {
        RuleFor(x => x.BlogPostId).NotEmpty();
        RuleFor(x => x.ImageUrl)
            .MaximumLength(2048)
            .Must(x => string.IsNullOrWhiteSpace(x) || Uri.TryCreate(x, UriKind.Absolute, out _))
            .WithMessage("ImageUrl must be an absolute URL.");
        RuleForEach(x => x.ImageUrls)
            .MaximumLength(2048)
            .Must(x => string.IsNullOrWhiteSpace(x) || Uri.TryCreate(x, UriKind.Absolute, out _))
            .WithMessage("ImageUrls must contain absolute URLs.");
        RuleFor(x => x.ImageAltText).MaximumLength(200);
    }
}

public sealed class UpdateBlogPostImageCommandHandler
    : IRequestHandler<UpdateBlogPostImageCommand, BlogPostDto>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;
    private readonly IBlogImageStorageService? _blogImageStorage;

    public UpdateBlogPostImageCommandHandler(
        IApplicationDbContext context,
        IUserContext userContext,
        IBlogImageStorageService? blogImageStorage = null)
    {
        _context = context;
        _userContext = userContext;
        _blogImageStorage = blogImageStorage;
    }

    public async Task<BlogPostDto> Handle(
        UpdateBlogPostImageCommand request,
        CancellationToken cancellationToken)
    {
        await BlogPostSupport.EnsureCurrentUserCanManageBlogPostsAsync(_context, _userContext, cancellationToken);

        var post = await _context.Set<BlogPost>()
            .Include(x => x.Author)
            .SingleOrDefaultAsync(x => x.Id == request.BlogPostId, cancellationToken)
            ?? throw new NotFoundException("Blog post not found.");

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
            imageUrls = BlogPostSupport.NormalizeImageUrls(
                request.ImageUrl,
                request.ImageUrls,
                nameof(request.ImageUrls));
        }

        BlogPostSupport.ApplyImageUrls(post, imageUrls);
        post.ImageAltText = BlogPostSupport.NormalizeOptionalText(
            request.ImageAltText,
            nameof(request.ImageAltText),
            200);

        BlogPostSupport.EnsurePublishedPostHasImage(post, nameof(request.ImageUrl));

        await _context.SaveChangesAsync(cancellationToken);

        return BlogPostSupport.ToDto(post);
    }
}
