using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Application.Knowledge;

/// <summary>
/// Danh sách knowledge cho màn quản lý (kể cả Draft). Không phân trang — knowledge base cỡ
/// vài chục entry, giống cách GetBlogPostManagementListQuery đang làm.
/// </summary>
[Authorize(Roles = "Admin")]
public sealed record GetKnowledgeEntriesAdminQuery(string? Status = null, string? Category = null)
    : IRequest<IReadOnlyList<KnowledgeEntryDto>>;

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
    }
}

public sealed class GetKnowledgeEntriesAdminQueryHandler
    : IRequestHandler<GetKnowledgeEntriesAdminQuery, IReadOnlyList<KnowledgeEntryDto>>
{
    private readonly IApplicationDbContext _context;

    public GetKnowledgeEntriesAdminQueryHandler(IApplicationDbContext context) => _context = context;

    public async Task<IReadOnlyList<KnowledgeEntryDto>> Handle(
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

        // Cùng thứ tự với danh sách công khai (DisplayOrder rồi ngày tạo) để admin nhìn màn
        // quản lý là biết web đang hiển thị theo thứ tự nào.
        var entries = await query
            .OrderBy(x => x.Category)
            .ThenBy(x => x.DisplayOrder)
            .ThenBy(x => x.Created)
            .ToListAsync(cancellationToken);

        return entries.Select(KnowledgeEntrySupport.ToDto).ToList();
    }
}
