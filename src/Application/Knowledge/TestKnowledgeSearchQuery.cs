using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Application.Knowledge;

[Authorize(Roles = "Admin")]
public sealed record TestKnowledgeSearchQuery(string Query, int Take = KnowledgeSearchSupport.DefaultTake)
    : IRequest<KnowledgeSearchTestResultDto>;

public sealed class TestKnowledgeSearchQueryValidator : AbstractValidator<TestKnowledgeSearchQuery>
{
    public TestKnowledgeSearchQueryValidator()
    {
        RuleFor(x => x.Query).NotEmpty().MaximumLength(500);
        RuleFor(x => x.Take).InclusiveBetween(1, KnowledgeSearchSupport.MaxTake);
    }
}

public sealed class TestKnowledgeSearchQueryHandler
    : IRequestHandler<TestKnowledgeSearchQuery, KnowledgeSearchTestResultDto>
{
    private const int MaxCorpusSize = 500;

    private readonly IApplicationDbContext _context;

    public TestKnowledgeSearchQueryHandler(IApplicationDbContext context) => _context = context;

    public async Task<KnowledgeSearchTestResultDto> Handle(
        TestKnowledgeSearchQuery request,
        CancellationToken cancellationToken)
    {
        var tokens = KnowledgeSearchSupport.Tokenize(request.Query);
        if (tokens.Length == 0)
        {
            return new KnowledgeSearchTestResultDto(request.Query, tokens, 0, []);
        }

        var entries = await _context.Set<KnowledgeEntry>()
            .AsNoTracking()
            // Phải khớp ĐÚNG bộ lọc của SearchKnowledgeQuery, không thì màn thử nghiệm của admin
            // cho kết quả khác cái trợ lý thật nhìn thấy.
            .Where(x => x.Status != KnowledgeEntry.DraftStatus)
            .OrderBy(x => x.DisplayOrder)
            .Take(MaxCorpusSize)
            .ToListAsync(cancellationToken);

        var requiredMatches = KnowledgeSearchSupport.GetRequiredMatchCount(tokens.Length);
        var matches = entries
            .Select(entry =>
            {
                var candidate = new KnowledgeSearchCandidate(
                    entry.Title,
                    entry.Content,
                    entry.Category,
                    entry.Keywords,
                    entry.DisplayOrder);
                return new { entry, score = KnowledgeSearchSupport.Score(candidate, tokens) };
            })
            .Where(x => KnowledgeSearchSupport.IsAccepted(x.score, requiredMatches))
            .OrderByDescending(x => x.score.Score)
            .ThenBy(x => x.entry.DisplayOrder)
            .ThenBy(x => x.entry.Title, StringComparer.Ordinal)
            .ToList();

        var limitedMatches = matches.Take(request.Take).ToList();
        var contents = KnowledgeSearchSupport.ApplyContentBudget(limitedMatches.Select(x => x.entry.Content));

        var hits = limitedMatches
            .Take(contents.Count)
            .Select((x, i) => new KnowledgeSearchTestHitDto(
                x.entry.Id,
                x.entry.Title,
                x.entry.Category,
                x.entry.Keywords,
                x.entry.DisplayOrder,
                x.score.Score,
                x.score.MatchedTokens,
                x.score.HasStrongKeywordHit,
                contents[i]))
            .ToList();

        return new KnowledgeSearchTestResultDto(request.Query, tokens, matches.Count, hits);
    }
}
