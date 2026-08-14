using SaigonWaterbus.Application.Knowledge;
using SaigonWaterbus.Domain.Constants;

namespace SaigonWaterbus.Web.Endpoints;

/// <summary>
/// Kho kien thuc, mot bang phuc vu HAI muc dich, phan biet bang status:
/// Published = chinh sach hien tren web VA tro ly doc duoc | Private = kien thuc noi bo, CHI
/// tro ly doc | Draft = dang soan, khong ai dung. Public route chi tra Published; quan ly chi Admin.
/// </summary>
public sealed class KnowledgeEntries : IEndpointGroup
{
    public static string RoutePrefix => "/api/knowledge-entries";

    private const string CreateExample =
        """
        {
          "title": "Chinh sach hoan ve nhu the nao?",
          "content": "Ve duoc hoan truoc gio khoi hanh 24 gio, phi hoan 10%...",
          "category": "Refund",
          "keywords": ["hoan ve", "tra lai ve", "huy ve", "refund"],
          "status": "Published",
          "displayOrder": 1
        }
        """;

    private const string StatusExample =
        """
        { "status": "Draft" }
        """;

    private const string TestSearchExample =
        """
        {
          "query": "toi muon hoan ve",
          "take": 3
        }
        """;

    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet(GetPublishedEntries, string.Empty)
            .AllowAnonymous()
            .WithSummary("Kien thuc cong khai (khach)")
            .WithDescription(OpenApiDescriptionBuilder.Build("Anonymous", null,
                "Tra ve cac muc dang Published de hien thi cho khach (chinh sach, quy dinh).",
                "Entry Draft va Private KHONG bao gio xuat hien o day — Private la kien thuc noi "
                + "bo chi danh cho tro ly.",
                "Query: category - bo trong la lay tat ca. "
                + $"Hop le: {string.Join(" | ", KnowledgeCategories.All)}.",
                "Sap xep: category, displayOrder, ngay tao.",
                "Khong tra ve keywords/status/author: keywords chi phuc vu tim kiem cua tro ly ao."));

        group.MapGet(GetEntries, "admin")
            .RequireAuthorization()
            .WithSummary("Danh sach kien thuc (admin)")
            .WithDescription(OpenApiDescriptionBuilder.Build("Admin", null,
                "Tra ve entry o MOI trang thai, kem noi dung day du, co phan trang.",
                "Query: status (Draft | Private | Published), category, keyword, page, pageSize.",
                $"Category hop le: {string.Join(" | ", KnowledgeCategories.All)}."));

        group.MapGet(GetMetadata, "metadata")
            .RequireAuthorization()
            .WithSummary("Metadata quan ly knowledge")
            .WithDescription(OpenApiDescriptionBuilder.Build("Admin", null,
                "Tra ve categories, statuses va cac gioi han de FE khong can fix cung."));

        group.MapPost(TestSearch, "admin/test-search")
            .RequireAuthorization()
            .WithSummary("Test search knowledge cho chatbot")
            .WithDescription(OpenApiDescriptionBuilder.Build("Admin", TestSearchExample,
                "Nhap cau hoi thu de xem chatbot se match entry nao.",
                "Dung dung bo loc cua chatbot that: ca Published lan Private, bo qua Draft.",
                "Tra ve token, score, matchedTokens va content da cat theo ngan sach context."));

        group.MapPost(CreateEntry, string.Empty)
            .RequireAuthorization()
            .WithSummary("Tao muc kien thuc")
            .WithDescription(OpenApiDescriptionBuilder.Build("Admin", CreateExample,
                "title = cau hoi hoac tieu de chu de; content = cau tra loi tro ly se doc.",
                "keywords RAT QUAN TRONG: tro ly tim bang KHOP TU, khong hieu tu dong nghia.",
                "status bo trong = Draft (khong ai dung). Muon tro ly dung thi dat Private (noi "
                + "bo, khong hien tren web) hoac Published (hien luon tren web).",
                "Tac gia lay tu token, khong nhan tu payload."));

        group.MapPut(UpdateEntry, "{id:guid}")
            .RequireAuthorization()
            .WithSummary("Cap nhat muc kien thuc")
            .WithDescription(OpenApiDescriptionBuilder.Build("Admin", CreateExample,
                "Full replace - gui lai toan bo, field bo trong se bi ghi de.",
                "Sua noi dung la tro ly dung ngay lap tuc, khong can deploy lai."));

        group.MapPut(UpdateEntryStatus, "{id:guid}/status")
            .RequireAuthorization()
            .WithSummary("Bat/tat hien thi cho tro ly")
            .WithDescription(OpenApiDescriptionBuilder.Build("Admin", StatusExample,
                "Chuyen ve Draft la cach rut ngay mot muc sai ra khoi CA web lan chatbot, khong can xoa. "
                + "Chuyen Published -> Private thi an khoi web nhung tro ly van dung."));

        group.MapDelete(DeleteEntry, "{id:guid}")
            .RequireAuthorization()
            .WithSummary("Xoa muc kien thuc")
            .WithDescription(OpenApiDescriptionBuilder.Build("Admin", null, "Tra ve 204."));
    }

    private static async Task<IResult> GetPublishedEntries(
        ISender sender, string? category, CancellationToken ct) =>
        Results.Ok(await sender.Send(new GetPublishedKnowledgeEntriesQuery(category), ct));

    private static async Task<IResult> GetEntries(
        ISender sender,
        string? status,
        string? category,
        string? keyword,
        int? page,
        int? pageSize,
        CancellationToken ct) =>
        Results.Ok(await sender.Send(new GetKnowledgeEntriesAdminQuery(
            status, category, keyword, page ?? 1, pageSize ?? 20), ct));

    private static async Task<IResult> GetMetadata(ISender sender, CancellationToken ct) =>
        Results.Ok(await sender.Send(new GetKnowledgeEntryMetadataQuery(), ct));

    private static async Task<IResult> TestSearch(
        ISender sender, TestKnowledgeSearchQuery request, CancellationToken ct) =>
        Results.Ok(await sender.Send(request, ct));

    private static async Task<IResult> CreateEntry(
        ISender sender, CreateKnowledgeEntryCommand command, CancellationToken ct) =>
        Results.Ok(await sender.Send(command, ct));

    private static async Task<IResult> UpdateEntry(
        ISender sender, Guid id, UpdateKnowledgeEntryRequest body, CancellationToken ct) =>
        Results.Ok(await sender.Send(new UpdateKnowledgeEntryCommand(
            id, body.Title, body.Content, body.Category,
            body.Keywords, body.Status, body.DisplayOrder), ct));

    private static async Task<IResult> UpdateEntryStatus(
        ISender sender, Guid id, UpdateKnowledgeEntryStatusRequest body, CancellationToken ct) =>
        Results.Ok(await sender.Send(new UpdateKnowledgeEntryStatusCommand(id, body.Status), ct));

    private static async Task<IResult> DeleteEntry(ISender sender, Guid id, CancellationToken ct)
    {
        await sender.Send(new DeleteKnowledgeEntryCommand(id), ct);
        return Results.NoContent();
    }

    public sealed record UpdateKnowledgeEntryRequest(
        string Title,
        string Content,
        string Category,
        IReadOnlyCollection<string>? Keywords,
        string? Status,
        int DisplayOrder);

    public sealed record UpdateKnowledgeEntryStatusRequest(string Status);
}
