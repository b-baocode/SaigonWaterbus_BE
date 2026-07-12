namespace SaigonWaterbus.Infrastructure.Auth;

public sealed class BrevoOptions
{
    public const string SectionName = "Brevo";

    public bool Enabled { get; set; }

    public string ApiBaseUrl { get; set; } = "https://api.brevo.com/v3";

    public string ApiKey { get; set; } = string.Empty;

    public string SenderEmail { get; set; } = string.Empty;

    public string SenderName { get; set; } = "SG_WATERBUS";

    public string? PublicApiBaseUrl { get; set; }

    public int TemplateId { get; set; }

    public int RegisterTemplateId { get; set; }

    public int LoginTemplateId { get; set; }

    public int ForgotPasswordTemplateId { get; set; }

    public int EmailChangeTemplateId { get; set; }

    public int CharterBookingQuoteTemplateId { get; set; }

    public int CharterBookingPaymentTemplateId { get; set; }

    public int CharterBookingConfirmationTemplateId { get; set; }

    public int BookingPaymentConfirmationTemplateId { get; set; }

    public int PaymentDepositTemplateId { get; set; }

    public int PaymentFullTemplateId { get; set; }

    /// <summary>Template Brevo cho email vé điện tử booking thường; 0 = dùng HTML inline.</summary>
    public int ETicketTemplateId { get; set; }
}
