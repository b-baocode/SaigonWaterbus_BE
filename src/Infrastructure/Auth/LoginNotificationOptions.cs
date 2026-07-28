namespace SaigonWaterbus.Infrastructure.Auth;

public sealed class LoginNotificationOptions
{
    public const string SectionName = "LoginNotification";

    public bool Enabled { get; set; }

    public int TemplateId { get; set; }

    public string WebName { get; set; } = "Waterbus";

    public string Subject { get; set; } = "Dang nhap thanh cong";
}
