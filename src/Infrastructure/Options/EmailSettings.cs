namespace SaigonWaterbus.Infrastructure.Options;

public class EmailSettings
{
    public const string SectionName = "Email";

    public string SenderEmail { get; set; } = string.Empty;

    public string SenderName { get; set; } = "Saigon Waterbus";

    public string AppPassword { get; set; } = string.Empty;

    public string SmtpHost { get; set; } = "smtp.gmail.com";

    public int SmtpPort { get; set; } = 587;

    public bool EnableSsl { get; set; } = true;
}
