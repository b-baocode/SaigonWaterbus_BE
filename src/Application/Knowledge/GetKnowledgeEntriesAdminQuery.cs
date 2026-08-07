using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Application.Knowledge;

/// <summary>Danh sach knowledge cho man quan ly cua Admin, gom ca Draft.</summary>
[Authorize(Roles = "Admin")]
public sealed record GetKnowledgeEntriesAdminQuery(
    string? Status = null,
    string? Category = null,
    string? Keyword = null,
    int Page = 1,
    int PageSize = 20)
    : IRequest<KnowledgeEntryListDto>;

public sealed class GetKnowledgeEntriesAdminQueryValidator : AbstractValidator<GetKnowledgeEntriesAdminQuery>
{
    public GetKnowledgeEntriesAdminQueryValidator()
    {
        RuleFor(x => x.Status)
            .Must(x => string.IsNullOrWhiteSpace(x) || KnowledgeEntry.IsValidStatus(x))
            .WithMessage("Status hop le: Draft | Published.");
        RuleFor(x => x.Category)
            .Must(x => string.IsNullOrWhiteSpace(x) || KnowledgeCategories.IsValid(x))
            .WithMessage($"Category hop le: {string.Join(" | ", KnowledgeCategories.All)}.");
        RuleFor(x => x.Page).GreaterThan(0);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
    }
}

public sealed class GetKnowledgeEntriesAdminQueryHandler
    : IRequestHandler<GetKnowledgeEntriesAdminQuery, KnowledgeEntryListDto>
{
    private readonly IApplicationDbContext _context;

    public GetKnowledgeEntriesAdminQueryHandler(IApplicationDbContext context) => _context = context;

    public async Task<KnowledgeEntryListDto> Handle(
        GetKnowledgeEntriesAdminQuery request,
        CancellationToken cancellationToken)
    {
        var query = _context.Set<KnowledgeEntry>().AsNoTracking();

        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            var status = KnowledgeEntrySupport.ResolveStatus(request.Status);
            query = query.Where(x => x.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(request.Category))
        {
            var category = KnowledgeEntrySupport.ResolveCategory(request.Category);
            query = query.Where(x => x.Category == category);
        }

        var orderedEntries = await query
            .OrderBy(x => x.Category)
            .ThenBy(x => x.DisplayOrder)
            .ThenBy(x => x.Created)
            .ToListAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            var keyword = request.Keyword.Trim();
            orderedEntries = orderedEntries
                .Where(x =>
                    x.Title.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                    || x.Content.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                    || x.Keywords.Any(k => k.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
                .ToList();
        }

        var totalCount = orderedEntries.Count;
        var entries = orderedEntries
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToList();

        return new KnowledgeEntryListDto(
            totalCount,
            request.Page,
            request.PageSize,
            entries.Select(KnowledgeEntrySupport.ToDto).ToList());
    }
}
