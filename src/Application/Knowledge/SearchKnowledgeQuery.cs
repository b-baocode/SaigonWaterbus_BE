using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Application.Knowledge;

/// <summary>
/// Tra knowledge base cho trợ lý ảo. KHÔNG có [Authorize] — trợ lý chạy ẩn danh; bù lại chỉ
/// đọc entry Published nên bản nháp của admin không bao giờ lọt ra cho khách.
/// </summary>
public sealed record SearchKnowledgeQuery(string Query, int Take = KnowledgeSearchSupport.DefaultTake)
    : IRequest<IReadOnlyList<KnowledgeSearchHitDto>>;

public sealed class SearchKnowledgeQueryHandler
    : IRequestHandler<SearchKnowledgeQuery, IReadOnlyList<KnowledgeSearchHitDto>>
{
    /// <summary>
    /// Chặn trên số entry nạp về để chấm điểm trong bộ nhớ. Ở quy mô vài chục–vài trăm entry
    /// (thực tế &lt;50 KB) thì nạp cả corpus mỗi lượt là rẻ hơn dựng full-text index. Nếu KB vượt
    /// mức này thì đổi sang full-text Postgres — logic khớp đã gom hết trong KnowledgeSearchSupport.
    /// </summary>
    private const int MaxCorpusSize = 500;

    private readonly IApplicationDbContext _context;

    public SearchKnowledgeQueryHandler(IApplicationDbContext context) => _context = context;

    public async Task<IReadOnlyList<KnowledgeSearchHitDto>> Handle(
        SearchKnowledgeQuery request,
        CancellationToken cancellationToken)
    {
        // Không có token nào dùng được thì khỏi phải đi DB.
        if (KnowledgeSearchSupport.Tokenize(request.Query).Length == 0)
        {
            return [];
        }

        var corpus = await _context.Set<KnowledgeEntry>()
            .AsNoTracking()
            .Where(x => x.Status == KnowledgeEntry.PublishedStatus)
            .OrderBy(x => x.DisplayOrder)
            .Take(MaxCorpusSize)
            .Select(x => new KnowledgeSearchCandidate(x.Title, x.Content, x.Category, x.Keywords, x.DisplayOrder))
            .ToListAsync(cancellationToken);

        return KnowledgeSearchSupport.Rank(corpus, request.Query, request.Take)
            .Select(x => new KnowledgeSearchHitDto(
                x.Title,
                x.Category,
                KnowledgeSearchSupport.TruncateContent(x.Content)))
            .ToList();
    }
}
