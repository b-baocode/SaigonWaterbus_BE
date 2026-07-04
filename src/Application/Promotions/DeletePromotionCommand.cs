using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;

namespace SaigonWaterbus.Application.Promotions;

[Authorize(Roles = "Admin")]
public sealed record DeletePromotionCommand(Guid PromotionId) : IRequest;

public sealed class DeletePromotionCommandValidator : AbstractValidator<DeletePromotionCommand>
{
    public DeletePromotionCommandValidator()
    {
        RuleFor(x => x.PromotionId).NotEmpty();
    }
}

public sealed class DeletePromotionCommandHandler : IRequestHandler<DeletePromotionCommand>
{
    private readonly IApplicationDbContext _context;

    public DeletePromotionCommandHandler(IApplicationDbContext context) => _context = context;

    public async Task Handle(DeletePromotionCommand request, CancellationToken cancellationToken)
    {
        var promotion = await _context.Set<Promotion>()
            .SingleOrDefaultAsync(p => p.Id == request.PromotionId, cancellationToken)
            ?? throw new NotFoundException("Promotion not found.");

        promotion.Status = "Inactive";
        await _context.SaveChangesAsync(cancellationToken);
    }
}
