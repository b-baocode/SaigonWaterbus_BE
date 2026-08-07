using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Application.Knowledge;

[Authorize(Roles = "Admin")]
public sealed record GetKnowledgeEntryMetadataQuery : IRequest<KnowledgeEntryMetadataDto>;

public sealed class GetKnowledgeEntryMetadataQueryHandler
    : IRequestHandler<GetKnowledgeEntryMetadataQuery, KnowledgeEntryMetadataDto>
{
    public Task<KnowledgeEntryMetadataDto> Handle(
        GetKnowledgeEntryMetadataQuery request,
        CancellationToken cancellationToken) =>
        Task.FromResult(new KnowledgeEntryMetadataDto(
            KnowledgeCategories.All,
            [KnowledgeEntry.DraftStatus, KnowledgeEntry.PublishedStatus],
            KnowledgeEntrySupport.MaxKeywords,
            KnowledgeEntrySupport.MaxKeywordLength,
            KnowledgeSearchSupport.MaxContentChars,
            KnowledgeSearchSupport.MaxTotalContentChars,
            KnowledgeSearchSupport.DefaultTake,
            KnowledgeSearchSupport.MaxTake));
}
