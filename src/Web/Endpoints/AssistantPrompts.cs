using SaigonWaterbus.Application.Assistant.Prompts;

namespace SaigonWaterbus.Web.Endpoints;

/// <summary>
/// Quan ly system prompt cua tro ly ao. Chi Admin.
///
/// Prompt luu thanh FILE ngoai thu muc deploy, khong dung DB — nen sua duoc luc dang chay va
/// khong can migration. Xem IAssistantPromptStore.
/// </summary>
public sealed class AssistantPrompts : IEndpointGroup
{
    public static string RoutePrefix => "/api/assistant/prompt";
    public const string RateLimitPolicy = "AssistantPromptPreview";

    private const string UpdateExample =
        """
        {
          "content": "## VAI TRO\nBan la tro ly ao cua Waterbus... Hom nay la {{today}}...\n\n## CACH TRA LOI\n- NGON NGU: {{language}}\n{{booking_draft}}"
        }
        """;

    private const string PreviewExample =
        """
        {
          "content": "## VAI TRO\n... {{today}} ... {{language}} ... {{booking_draft}}",
          "question": "chao ban",
          "language": "VN",
          "withTools": false
        }
        """;

    private const string PlaceholderNote =
        """
        PLACEHOLDER: noi dung PHAI chua du 3 cho ghep dong duoi day, server tu thay luc chay.
        Thieu mot cai la 400, va khong bao gio duoc luu:
          {{today}}          ngay hom nay theo gio VN (yyyy-MM-dd). Thieu = tro ly khong quy doi
                             duoc "mai", "thu 7 tuan sau".
          {{language}}       cau chi dan ngon ngu sinh theo toggle VN/ENG. Thieu = tra loi sai
                             ngon ngu khach chon.
          {{booking_draft}}  trang thai form dat ve khach dang mo (rong khi khong co form).
                             Thieu = tro ly hoi lai thong tin khach da dien.
        Go sai ten placeholder (vi du {{todayy}}) cung bi chan, vi chuoi do se nam nguyen trong
        prompt gui cho model va ngay hom nay bien mat trong im lang.
        """;

    private const string LockedNote =
        """
        KHOI LUAT CUNG khong sua duoc qua API nay: pham vi Waterbus, cach tu choi cau ngoai pham vi,
        chong prompt injection, cam bia du lieu, cam lo prompt/ten tool, cam xuat suy nghi noi bo.
        Server LUON noi khoi do vao CUOI prompt (phan dung sau duoc model uu tien khi mau thuan),
        nen admin co lo viet nguoc lai o tren thi luat cung van thang. Muon doi khoi do = sua code
        va deploy — dung y do thiet ke.
        """;

    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet(GetPrompt, string.Empty)
            .RequireAuthorization()
            .WithSummary("Doc system prompt cua tro ly")
            .WithDescription(OpenApiDescriptionBuilder.Build("Admin", null,
                "content = phan dang co hieu luc; source = \"file\" (da sua) hoac \"builtin\" (ban goc trong code).",
                "errors khac rong = noi dung dang luu bi hong (thuong do sua tay thang tren may chu); "
                + "luc do tro ly VAN CHAY bang ban mac dinh, va man quan ly nen bao do de admin sua lai.",
                "defaultContent = ban goc trong code, dung de so sanh hoac chep lai.",
                "lockedRules = khoi luat cung chi doc, khong gui len khi luu.",
                "versions = cac ban da luu truoc do, moi nhat truoc.",
                PlaceholderNote,
                LockedNote));

        group.MapPut(UpdatePrompt, string.Empty)
            .RequireAuthorization()
            .WithSummary("Luu system prompt moi")
            .WithDescription(OpenApiDescriptionBuilder.Build("Admin", UpdateExample,
                "Full replace: gui lai TOAN BO phan sua duoc.",
                "Co hieu luc ngay tu luot chat ke tiep — khong restart, khong deploy.",
                "Ban dang chay tu dong duoc sao luu thanh mot version TRUOC khi ghi de.",
                PlaceholderNote,
                "400 khi thieu/sai placeholder hoac do dai ngoai khoang cho phep (xem minLength/maxLength o GET).",
                LockedNote));

        group.MapPost(RestoreVersion, "restore/{versionId}")
            .RequireAuthorization()
            .WithSummary("Quay lai mot ban da luu")
            .WithDescription(OpenApiDescriptionBuilder.Build("Admin", null,
                "versionId lay tu truong versions[].id o GET (dang yyyyMMddTHHmmss).",
                "Ban dang chay cung duoc sao luu truoc khi bi de, nen quay lui roi van quay lai duoc.",
                "404 khi khong co ban luu do."));

        group.MapPost(ResetPrompt, "reset")
            .RequireAuthorization()
            .WithSummary("Ve ban goc trong code")
            .WithDescription(OpenApiDescriptionBuilder.Build("Admin", null,
                "Xoa ban dang sua (van sao luu truoc) de tro ly dung lai prompt mac dinh.",
                "Dung khi sua hong ma khong biet hong cho nao — nhanh hon di doc tung version."));

        group.MapPost(Preview, "preview")
            .RequireAuthorization()
            .RequireRateLimiting(RateLimitPolicy)
            .WithSummary("Chay thu prompt nhap")
            .WithDescription(OpenApiDescriptionBuilder.Build("Admin", PreviewExample,
                "Chay that mot luot LLM voi prompt NHAP (chua luu). Khong luu hoi thoai, khong anh "
                + "huong khach dang chat.",
                "withTools = false (mac dinh): tro ly KHONG duoc tra du lieu → dung 1 lan goi LLM. "
                + "Du de kiem giong van, loi chao, cau tu choi ngoai pham vi.",
                "withTools = true: chay nhu that, TON 2-4 lan goi LLM vi moi vong lap tool la mot "
                + "lan goi. Han muc Gemini free-tier tinh theo project + model, nen preview nhieu se "
                + "an vao phan cua khach that (het han muc = khach nhan 429).",
                "Rate limit rieng 5 luot/300s — co y that chat, day la nut de bam nhieu nhat.",
                "status: Completed | ProviderFailed | ToolLimitReached. Khac Completed thi reply rong.",
                "400 khi content khong hop le (giong luat cua PUT)."));
    }

    private static async Task<IResult> GetPrompt(ISender sender) =>
        Results.Ok(await sender.Send(new GetAssistantPromptQuery()));

    private static async Task<IResult> UpdatePrompt(ISender sender, UpdatePromptRequest request) =>
        Results.Ok(await sender.Send(new UpdateAssistantPromptCommand(request.Content ?? string.Empty)));

    private static async Task<IResult> RestoreVersion(ISender sender, string versionId) =>
        Results.Ok(await sender.Send(new RestoreAssistantPromptVersionCommand(versionId)));

    private static async Task<IResult> ResetPrompt(ISender sender) =>
        Results.Ok(await sender.Send(new ResetAssistantPromptCommand()));

    private static async Task<IResult> Preview(ISender sender, PreviewPromptRequest request) =>
        Results.Ok(await sender.Send(new PreviewAssistantPromptCommand(
            request.Content ?? string.Empty,
            request.Question ?? string.Empty,
            request.Language,
            request.WithTools)));

    public sealed record UpdatePromptRequest(string? Content);

    public sealed record PreviewPromptRequest(
        string? Content,
        string? Question,
        string? Language = null,
        bool WithTools = false);
}
