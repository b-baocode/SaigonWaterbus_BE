using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using SaigonWaterbus.Application.Knowledge;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Infrastructure.Data;
using Shouldly;

namespace SaigonWaterbus.Application.UnitTests.Knowledge;

public class KnowledgeAdminQueryTests
{
    [Test]
    public async Task AdminListFiltersByStatusCategoryKeywordAndPaginates()
    {
        await using var context = CreateContext();
        context.KnowledgeEntries.AddRange(
            Entry("Refund policy", "Refund content", KnowledgeCategories.Refund, KnowledgeEntry.PublishedStatus, ["refund"], 1),
            Entry("Luggage rules", "Bring bags", KnowledgeCategories.Luggage, KnowledgeEntry.DraftStatus, ["bags"], 2),
            Entry("Payment guide", "Pay by card", KnowledgeCategories.Payment, KnowledgeEntry.PublishedStatus, ["card"], 3));
        await context.SaveChangesAsync();

        var handler = new GetKnowledgeEntriesAdminQueryHandler(context);

        var result = await handler.Handle(
            new GetKnowledgeEntriesAdminQuery(
                Status: KnowledgeEntry.PublishedStatus,
                Category: KnowledgeCategories.Payment,
                Keyword: "card",
                Page: 1,
                PageSize: 10),
            CancellationToken.None);

        result.TotalCount.ShouldBe(1);
        result.Items.ShouldHaveSingleItem().Title.ShouldBe("Payment guide");
    }

    [Test]
    public async Task TestSearchOnlyUsesPublishedEntriesAndReturnsDiagnostics()
    {
        await using var context = CreateContext();
        context.KnowledgeEntries.AddRange(
            Entry("Refund published", "Published refund answer", KnowledgeCategories.Refund, KnowledgeEntry.PublishedStatus, ["refund"], 1),
            Entry("Refund draft", "Draft refund answer", KnowledgeCategories.Refund, KnowledgeEntry.DraftStatus, ["refund"], 0));
        await context.SaveChangesAsync();

        var handler = new TestKnowledgeSearchQueryHandler(context);

        var result = await handler.Handle(new TestKnowledgeSearchQuery("refund", 3), CancellationToken.None);

        result.Tokens.ShouldContain("refund");
        result.TotalMatched.ShouldBe(1);
        var hit = result.Hits.ShouldHaveSingleItem();
        hit.Title.ShouldBe("Refund published");
        hit.Score.ShouldBeGreaterThan(0);
        hit.ContentSeenByAssistant.ShouldBe("Published refund answer");
    }

    [Test]
    public async Task MetadataReturnsAdminFormOptionsAndSearchLimits()
    {
        var result = await new GetKnowledgeEntryMetadataQueryHandler()
            .Handle(new GetKnowledgeEntryMetadataQuery(), CancellationToken.None);

        result.Categories.ShouldContain(KnowledgeCategories.Refund);
        result.Statuses.ShouldBe([KnowledgeEntry.DraftStatus, KnowledgeEntry.PublishedStatus]);
        result.MaxKeywords.ShouldBe(KnowledgeEntrySupport.MaxKeywords);
        result.MaxSearchTake.ShouldBe(KnowledgeSearchSupport.MaxTake);
    }

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"knowledge-admin-{Guid.NewGuid():N}")
            .Options;

        return new ApplicationDbContext(options);
    }

    private static KnowledgeEntry Entry(
        string title,
        string content,
        string category,
        string status,
        string[] keywords,
        int displayOrder) =>
        new()
        {
            Title = title,
            Content = content,
            Category = category,
            Status = status,
            Keywords = keywords,
            DisplayOrder = displayOrder,
            AuthorId = Guid.NewGuid(),
            Created = DateTimeOffset.UtcNow.AddMinutes(displayOrder)
        };
}
