using SaigonWaterbus.Application.Knowledge;
using SaigonWaterbus.Domain.Constants;

namespace SaigonWaterbus.Web.Endpoints;

/// <summary>
/// Kho kiến thức (chính sách, quy định, hướng dẫn). Hai nhóm route:
/// - GET "" : CÔNG KHAI, chỉ trả entry Published để web hiển thị cho khách.
/// - Còn lại: quản lý, chỉ Admin.
///
/// Trợ lý ảo KHÔNG đi qua HTTP: tool search_knowledge gọi thẳng MediatR trong process.
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

    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet(GetPublishedEntries, string.Empty)
            .AllowAnonymous()
            .WithSummary("Kien thuc cong khai (khach)")
            .WithDescription(OpenApiDescriptionBuilder.Build("Anonymous", null,
                "Tra ve cac muc kien thuc dang Published de hien thi cho khach (chinh sach, quy dinh, huong dan).",
                "Entry dang Draft KHONG bao gio xuat hien o day.",
                "Query: category — bo trong la lay tat ca. "
                + $"Hop le: {string.Join(" | ", KnowledgeCategories.All)}.",
                "Sap xep: category, roi displayOrder, roi NGAY TAO (khong phai tieu de) — muc them "
                + "sau luon nam cuoi, khong hat muc dang hien thi o vi tri co dinh tren web.",
                "QUY UOC VAN HANH: moi category nen chi co MOT muc chinh (displayOrder nho nhat). "
                + "FE render vi tri co dinh (vi du o chinh sach o trang thanh toan) se lay theo "
                + "category, nen them muc thu hai cung category la khach thay ca hai.",
                "Khong tra ve keywords/status/author: keywords chi phuc vu tim kiem cua tro ly ao."));

        group.MapGet(GetEntries, "admin")
            .RequireAuthorization()
            .WithSummary("Danh sach kien thuc (admin)")
            .WithDescription(OpenApiDescriptionBuilder.Build("Admin", null,
                "Tra ve tat ca entry ke ca Draft, kem noi dung day du. Khong phan trang.",
                "Query: status (Draft | Published), category — bo trong la lay tat ca.",
                $"Category hop le: {string.Join(" | ", KnowledgeCategories.All)}."));

        group.MapPost(CreateEntry, string.Empty)
            .RequireAuthorization()
            .WithSummary("Tao muc kien thuc")
            .WithDescription(OpenApiDescriptionBuilder.Build("Admin", CreateExample,
                "title = cau hoi hoac tieu de chu de; content = cau tra loi tro ly se doc.",
                "keywords RAT QUAN TRONG: tro ly tim bang KHOP TU, khong hieu tu dong nghia. "
                + "Khach hoi 'tra lai ve' se khong khop entry 'hoan ve' neu khong khai tu khoa. "
                + "Hay liet ke moi cach khach hay hoi ve chu de nay.",
                "DO DAI content: moi category nen chi MOT muc gom het noi dung, va giu duoi ~4000 "
                + "ky tu. Web hien thi day du, nhung tro ly ao chi doc 4000 ky tu dau — viet dai hon "
                + "thi phan duoi bot KHONG nhin thay du khach van doc duoc tren web.",
                "status bo trong = Draft; chi entry Published moi duoc tro ly dung.",
                "Tac gia lay tu token, khong nhan tu payload."));

        group.MapPut(UpdateEntry, "{id:guid}")
            .RequireAuthorization()
            .WithSummary("Cap nhat muc kien thuc")
            .WithDescription(OpenApiDescriptionBuilder.Build("Admin", CreateExample,
                "Full replace — gui lai toan bo, field bo trong se bi ghi de.",
                "Sua noi dung la tro ly dung ngay lap tuc, khong can deploy lai."));

        group.MapPut(UpdateEntryStatus, "{id:guid}/status")
            .RequireAuthorization()
            .WithSummary("Bat/tat hien thi cho tro ly")
            .WithDescription(OpenApiDescriptionBuilder.Build("Admin", StatusExample,
                "Chuyen ve Draft la cach rut ngay mot muc kien thuc sai ra khoi chatbot, khong can xoa."));

        group.MapDelete(DeleteEntry, "{id:guid}")
            .RequireAuthorization()
            .WithSummary("Xoa muc kien thuc")
            .WithDescription(OpenApiDescriptionBuilder.Build("Admin", null, "Tra ve 204."));
    }

    private static async Task<IResult> GetPublishedEntries(
        ISender sender, string? category, CancellationToken ct) =>
        Results.Ok(await sender.Send(new GetPublishedKnowledgeEntriesQuery(category), ct));

    private static async Task<IResult> GetEntries(
        ISender sender, string? status, string? category, CancellationToken ct) =>
        Results.Ok(await sender.Send(new GetKnowledgeEntriesAdminQuery(status, category), ct));

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
