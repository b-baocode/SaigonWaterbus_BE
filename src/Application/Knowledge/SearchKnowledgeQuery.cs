using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Application.Knowledge;

/// <summary>
/// Tra knowledge base cho trợ lý ảo. KHÔNG có [Authorize] — trợ lý chạy ẩn danh.
///
/// Đọc CẢ HAI: <c>Public</c> (chính sách đang hiển thị trên web) lẫn <c>Private</c> (kiến thức
/// nội bộ chỉ trợ lý dùng) — nhờ vậy chính sách không phải chép ra làm hai bản rồi lệch nhau.
/// Riêng <c>Draft</c> thì KHÔNG: đó là bản đang soạn, chưa ai duyệt, trợ lý nói ra là nói thay
/// công ty một điều chưa được chốt.
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
            .Where(x => x.Status != KnowledgeEntry.DraftStatus)
            .OrderBy(x => x.DisplayOrder)
            .Take(MaxCorpusSize)
            .Select(x => new KnowledgeSearchCandidate(x.Title, x.Content, x.Category, x.Keywords, x.DisplayOrder))
            .ToListAsync(cancellationToken);

        var ranked = KnowledgeSearchSupport.Rank(corpus, request.Query, request.Take);

        // Cắt theo hạn mức TỔNG, không phải từng hit riêng lẻ: hit xếp trước giữ nguyên, hết
        // ngân sách thì bỏ hẳn hit sau. Danh sách trả về có thể ngắn hơn ranked nên phải Take.
        var contents = KnowledgeSearchSupport.ApplyContentBudget(ranked.Select(x => x.Content));

        return ranked
            .Take(contents.Count)
            .Select((x, i) => new KnowledgeSearchHitDto(x.Title, x.Category, contents[i]))
            .ToList();
    }
}
