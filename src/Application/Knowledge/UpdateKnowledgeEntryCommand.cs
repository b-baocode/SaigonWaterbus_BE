using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;

namespace SaigonWaterbus.Application.Knowledge;

/// <summary>Full replace — gửi lại toàn bộ nội dung, field bỏ trống sẽ bị ghi đè (giống UpdateLandmarkCommand).</summary>
[Authorize(Roles = "Admin")]
public sealed record UpdateKnowledgeEntryCommand(
    Guid KnowledgeEntryId,
    string Title,
    string Content,
    string Category,
    IReadOnlyCollection<string>? Keywords = null,
    string? Status = null,
    int DisplayOrder = 0) : IRequest<KnowledgeEntryDto>;

public sealed class UpdateKnowledgeEntryCommandValidator : AbstractValidator<UpdateKnowledgeEntryCommand>
{
    public UpdateKnowledgeEntryCommandValidator()
    {
        RuleFor(x => x.KnowledgeEntryId).NotEmpty();
        RuleFor(x => x.Title).NotEmpty().MaximumLength(300);
        RuleFor(x => x.Content).NotEmpty();
        RuleFor(x => x.Category)
            .NotEmpty()
            .Must(KnowledgeCategories.IsValid)
            .WithMessage($"Category hop le: {string.Join(" | ", KnowledgeCategories.All)}.");
        RuleFor(x => x.Status)
            .Must(x => string.IsNullOrWhiteSpace(x) || KnowledgeEntry.IsValidStatus(x))
            .WithMessage("Status hop le: Draft | Published.");
        RuleFor(x => x.Keywords)
            .Must(KnowledgeEntrySupport.IsKeywordCountValid)
            .WithMessage($"Toi da {KnowledgeEntrySupport.MaxKeywords} tu khoa.")
            .Must(KnowledgeEntrySupport.IsKeywordLengthValid)
            .WithMessage($"Moi tu khoa toi da {KnowledgeEntrySupport.MaxKeywordLength} ky tu.");
        RuleFor(x => x.DisplayOrder).GreaterThanOrEqualTo(0);
    }
}

public sealed class UpdateKnowledgeEntryCommandHandler
    : IRequestHandler<UpdateKnowledgeEntryCommand, KnowledgeEntryDto>
{
    private readonly IApplicationDbContext _context;
    private readonly TimeProvider _timeProvider;

    public UpdateKnowledgeEntryCommandHandler(IApplicationDbContext context, TimeProvider timeProvider)
    {
        _context = context;
        _timeProvider = timeProvider;
    }

    public async Task<KnowledgeEntryDto> Handle(
        UpdateKnowledgeEntryCommand request,
        CancellationToken cancellationToken)
    {
        var entry = await _context.Set<KnowledgeEntry>()
            .SingleOrDefaultAsync(x => x.Id == request.KnowledgeEntryId, cancellationToken)
            ?? throw new NotFoundException("Knowledge entry not found.");

        entry.Title = request.Title.Trim();
        entry.Content = request.Content.Trim();
        entry.Category = KnowledgeEntrySupport.ResolveCategory(request.Category);
        entry.Keywords = KnowledgeEntrySupport.SanitizeKeywords(request.Keywords);
        entry.Status = KnowledgeEntrySupport.ResolveStatus(request.Status);
        entry.DisplayOrder = request.DisplayOrder;
        entry.UpdatedAt = _timeProvider.GetUtcNow();

        await _context.SaveChangesAsync(cancellationToken);
        return KnowledgeEntrySupport.ToDto(entry);
    }
}
