using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;

namespace SaigonWaterbus.Application.Promotions;

public sealed record UpdatePromotionCommand(
    Guid PromotionId,
    string PromotionName,
    decimal DiscountValue,
    decimal? MinOrderValue,
    DateTimeOffset ValidFrom,
    DateTimeOffset ValidTo,
    int? UsageLimit,
    string Status) : IRequest<PromotionDto>;

public sealed class UpdatePromotionCommandValidator : AbstractValidator<UpdatePromotionCommand>
{
    public UpdatePromotionCommandValidator()
    {
        RuleFor(x => x.PromotionId).NotEmpty();
        RuleFor(x => x.PromotionName).NotEmpty().MaximumLength(150);
        RuleFor(x => x.DiscountValue).GreaterThan(0);
        RuleFor(x => x.ValidTo).GreaterThan(x => x.ValidFrom);
        RuleFor(x => x.Status).NotEmpty();
    }
}

public sealed class UpdatePromotionCommandHandler : IRequestHandler<UpdatePromotionCommand, PromotionDto>
{
    private readonly IApplicationDbContext _context;

    public UpdatePromotionCommandHandler(IApplicationDbContext context) => _context = context;

    public async Task<PromotionDto> Handle(UpdatePromotionCommand request, CancellationToken cancellationToken)
    {
        var promotion = await _context.Set<Promotion>()
            .SingleOrDefaultAsync(p => p.Id == request.PromotionId, cancellationToken)
            ?? throw new NotFoundException("Promotion not found.");

        promotion.PromotionName = request.PromotionName.Trim();
        promotion.DiscountValue = request.DiscountValue;
        promotion.MinOrderValue = request.MinOrderValue;
        promotion.ValidFrom = request.ValidFrom;
        promotion.ValidTo = request.ValidTo;
        promotion.UsageLimit = request.UsageLimit;
        promotion.Status = request.Status;

        await _context.SaveChangesAsync(cancellationToken);

        return new PromotionDto(promotion.Id, promotion.PromotionCode, promotion.PromotionName,
            promotion.PromotionType, promotion.DiscountValue, promotion.MinOrderValue,
            promotion.ValidFrom, promotion.ValidTo, promotion.UsageLimit, promotion.UsageCount, promotion.Status);
    }
}
