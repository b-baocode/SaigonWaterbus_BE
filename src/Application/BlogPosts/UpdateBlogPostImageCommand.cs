using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;

namespace SaigonWaterbus.Application.BlogPosts;

public sealed record UpdateBlogPostImageCommand(
    Guid BlogPostId,
    string? ImageUrl,
    string? ImageAltText = null,
    BlogPostImageFileRequest? ImageFile = null) : IRequest<BlogPostDto>;

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

        post.ImageUrl = request.ImageFile is null
            ? BlogPostSupport.NormalizeImageUrl(request.ImageUrl, nameof(request.ImageUrl))
            : await BlogPostSupport.UploadImageAsync(
                post.Id,
                request.ImageFile,
                _blogImageStorage,
                nameof(request.ImageFile),
                cancellationToken);
        post.ImageAltText = BlogPostSupport.NormalizeOptionalText(
            request.ImageAltText,
            nameof(request.ImageAltText),
            200);

        BlogPostSupport.EnsurePublishedPostHasImage(post, nameof(request.ImageUrl));

        await _context.SaveChangesAsync(cancellationToken);

        return BlogPostSupport.ToDto(post);
    }
}
