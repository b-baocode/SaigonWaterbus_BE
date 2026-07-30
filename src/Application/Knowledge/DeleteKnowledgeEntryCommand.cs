using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;

namespace SaigonWaterbus.Application.Knowledge;

[Authorize(Roles = "Admin")]
public sealed record DeleteKnowledgeEntryCommand(Guid KnowledgeEntryId) : IRequest;

public sealed class DeleteKnowledgeEntryCommandValidator : AbstractValidator<DeleteKnowledgeEntryCommand>
{
    public DeleteKnowledgeEntryCommandValidator()
    {
        RuleFor(x => x.KnowledgeEntryId).NotEmpty();
    }
}

public sealed class DeleteKnowledgeEntryCommandHandler : IRequestHandler<DeleteKnowledgeEntryCommand>
{
    private readonly IApplicationDbContext _context;

    public DeleteKnowledgeEntryCommandHandler(IApplicationDbContext context) => _context = context;

    public async Task Handle(DeleteKnowledgeEntryCommand request, CancellationToken cancellationToken)
    {
        var entry = await _context.Set<KnowledgeEntry>()
            .SingleOrDefaultAsync(x => x.Id == request.KnowledgeEntryId, cancellationToken)
            ?? throw new NotFoundException("Knowledge entry not found.");

        _context.Set<KnowledgeEntry>().Remove(entry);
        await _context.SaveChangesAsync(cancellationToken);
    }
}
