namespace SaigonWaterbus.Infrastructure.Ai;

public sealed class AssistantPromptOptions
{
    public const string SectionName = "AssistantPrompt";

    /// <summary>
    /// Thư mục chứa file prompt. Bỏ trống thì tự chọn (xem <see cref="FileAssistantPromptStore"/>):
    /// trên Azure App Service là <c>%HOME%/data/prompts</c> — thư mục này persistent, ghi được, và
    /// KHÔNG bị lần deploy sau ghi đè như <c>site/wwwroot</c>.
    /// </summary>
    public string? Directory { get; set; }

    /// <summary>Số bản sao lưu giữ lại; cũ hơn thì xoá dần cho khỏi rác thư mục.</summary>
    public int MaxVersions { get; set; } = 20;
}
