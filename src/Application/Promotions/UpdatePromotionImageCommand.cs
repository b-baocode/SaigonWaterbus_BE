using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;

namespace SaigonWaterbus.Application.Promotions;

[Authorize(Roles = "Admin")]
public sealed record UpdatePromotionImageCommand(
    Guid PromotionId,
    string? ImageUrl,
    PromotionImageFileRequest? ImageFile = null) : IRequest<PromotionDto>;

public sealed class UpdatePromotionImageCommandValidator : AbstractValidator<UpdatePromotionImageCommand>
{
    public UpdatePromotionImageCommandValidator()
    {
        RuleFor(x => x.PromotionId).NotEmpty();
        RuleFor(x => x.ImageUrl)
            .MaximumLength(2048)
            .Must(x => string.IsNullOrWhiteSpace(x) || Uri.TryCreate(x, UriKind.Absolute, out _))
            .WithMessage("ImageUrl must be an absolute URL.");
    }
}

public sealed class UpdatePromotionImageCommandHandler : IRequestHandler<UpdatePromotionImageCommand, PromotionDto>
{
    private readonly IApplicationDbContext _context;
    private readonly TimeProvider _timeProvider;
    private readonly IPromotionImageStorageService? _imageStorage;

    public UpdatePromotionImageCommandHandler(
        IApplicationDbContext context,
        TimeProvider timeProvider,
        IPromotionImageStorageService? imageStorage = null)
    {
        _context = context;
        _timeProvider = timeProvider;
        _imageStorage = imageStorage;
    }

    public async Task<PromotionDto> Handle(UpdatePromotionImageCommand request, CancellationToken cancellationToken)
    {
        var promotion = await _context.Set<Promotion>()
            .SingleOrDefaultAsync(p => p.Id == request.PromotionId, cancellationToken)
            ?? throw new NotFoundException("Promotion not found.");

        if (request.ImageFile is not null)
        {
            var stored = await PromotionSupport.UploadImageAsync(
                promotion.Id, request.ImageFile, _imageStorage, nameof(request.ImageFile), cancellationToken);
            promotion.ImageUrl = stored.Url;
            promotion.ImagePublicId = stored.PublicId;
        }
        else
        {
            promotion.ImageUrl = PromotionSupport.NormalizeImageUrl(request.ImageUrl, nameof(request.ImageUrl));
            promotion.ImagePublicId = null;
        }

        await _context.SaveChangesAsync(cancellationToken);

        var (totalUsed, budgetSpent) = await PromotionSupport.GetUsageAsync(_context, promotion.Id, cancellationToken);
        return PromotionSupport.ToDto(promotion, _timeProvider.GetUtcNow(), totalUsed, budgetSpent);
    }
}
