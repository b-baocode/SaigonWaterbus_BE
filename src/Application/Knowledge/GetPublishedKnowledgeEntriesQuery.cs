using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Application.Knowledge;

/// <summary>
/// Danh sách kiến thức CÔNG KHAI cho khách xem (chính sách, quy định, hướng dẫn).
/// KHÔNG có [Authorize] — đây là nội dung để hiển thị trên web.
///
/// CHỈ trả entry <c>Public</c>. Đây là chỗ duy nhất phân biệt với <see cref="SearchKnowledgeQuery"/>:
/// trợ lý đọc thêm cả <c>Private</c> (kiến thức nội bộ), còn trang web thì không — nội dung nội bộ
/// lọt ra đây là rò rỉ, nên điều kiện lọc ở đây phải là so sánh BẰNG Public chứ không phải
/// "khác Draft".
/// </summary>
public sealed record GetPublishedKnowledgeEntriesQuery(string? Category = null)
    : IRequest<IReadOnlyList<PublicKnowledgeEntryDto>>;

public sealed class GetPublishedKnowledgeEntriesQueryValidator
    : AbstractValidator<GetPublishedKnowledgeEntriesQuery>
{
    public GetPublishedKnowledgeEntriesQueryValidator()
    {
        RuleFor(x => x.Category)
            .Must(x => string.IsNullOrWhiteSpace(x) || KnowledgeCategories.IsValid(x))
            .WithMessage($"Category hop le: {string.Join(" | ", KnowledgeCategories.All)}.");
    }
}

public sealed class GetPublishedKnowledgeEntriesQueryHandler
    : IRequestHandler<GetPublishedKnowledgeEntriesQuery, IReadOnlyList<PublicKnowledgeEntryDto>>
{
    private readonly IApplicationDbContext _context;

    public GetPublishedKnowledgeEntriesQueryHandler(IApplicationDbContext context) => _context = context;

    public async Task<IReadOnlyList<PublicKnowledgeEntryDto>> Handle(
        GetPublishedKnowledgeEntriesQuery request,
        CancellationToken cancellationToken)
    {
        var query = _context.Set<KnowledgeEntry>()
            .AsNoTracking()
            .Where(x => x.Status == KnowledgeEntry.PublishedStatus);

        if (!string.IsNullOrWhiteSpace(request.Category))
        {
            var category = KnowledgeEntrySupport.ResolveCategory(request.Category);
            query = query.Where(x => x.Category == category);
        }

        // Trùng DisplayOrder thì sắp theo NGÀY TẠO, cố ý KHÔNG theo tiêu đề.
        // Sắp theo tiêu đề thì một mục mới đặt tên bắt đầu bằng "A" sẽ nhảy lên đầu và
        // chiếm chỗ mục đang hiển thị ở ô cố định trên web — đổi nội dung khách đang thấy
        // mà không ai hay. Theo ngày tạo thì mục mới luôn nằm cuối, mục cũ giữ nguyên vị trí.
        return await query
            .OrderBy(x => x.Category)
            .ThenBy(x => x.DisplayOrder)
            .ThenBy(x => x.Created)
            .Select(x => new PublicKnowledgeEntryDto(
                x.Id, x.Title, x.Content, x.Category, x.DisplayOrder, x.UpdatedAt))
            .ToListAsync(cancellationToken);
    }
}
