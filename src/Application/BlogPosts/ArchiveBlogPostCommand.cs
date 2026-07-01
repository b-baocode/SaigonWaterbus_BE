using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;

namespace SaigonWaterbus.Application.BlogPosts;

public sealed record ArchiveBlogPostCommand(Guid BlogPostId) : IRequest;

public sealed class ArchiveBlogPostCommandValidator : AbstractValidator<ArchiveBlogPostCommand>
{
    public ArchiveBlogPostCommandValidator()
    {
        RuleFor(x => x.BlogPostId).NotEmpty();
    }
}

public sealed class ArchiveBlogPostCommandHandler : IRequestHandler<ArchiveBlogPostCommand>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;

    public ArchiveBlogPostCommandHandler(IApplicationDbContext context, IUserContext userContext)
    {
        _context = context;
        _userContext = userContext;
    }

    public async Task Handle(ArchiveBlogPostCommand request, CancellationToken cancellationToken)
    {
        await BlogPostSupport.EnsureCurrentUserCanManageBlogPostsAsync(_context, _userContext, cancellationToken);

        var post = await _context.Set<BlogPost>()
            .SingleOrDefaultAsync(x => x.Id == request.BlogPostId, cancellationToken)
            ?? throw new NotFoundException("Blog post not found.");

        post.Status = BlogPostSupport.ArchivedStatus;
        await _context.SaveChangesAsync(cancellationToken);
    }
}
