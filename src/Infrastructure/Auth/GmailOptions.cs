namespace SaigonWaterbus.Infrastructure.Auth;

public sealed class GmailOptions
{
    public const string SectionName = "Gmail";

    public bool Enabled { get; set; }
    public string Host { get; set; } = "smtp.gmail.com";
    public int Port { get; set; } = 587;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string FromEmail { get; set; } = string.Empty;
    public string FromName { get; set; } = "Saigon Waterbus";
    public string RegisterSubject { get; set; } = "Ma OTP dang ky Saigon Waterbus";
    public string LoginSubject { get; set; } = "Ma OTP dang nhap Saigon Waterbus";
    public string ForgotPasswordSubject { get; set; } = "Ma OTP quen mat khau Saigon Waterbus";
    public string RegisterTemplate { get; set; } =
        "Ma OTP dang ky Saigon Waterbus cua ban la {code}. Hieu luc {ttl_minutes} phut.";
    public string LoginTemplate { get; set; } =
        "Ma OTP dang nhap Saigon Waterbus cua ban la {code}. Hieu luc {ttl_minutes} phut.";
    public string ForgotPasswordTemplate { get; set; } =
        "Ma OTP quen mat khau Saigon Waterbus cua ban la {code}. Hieu luc {ttl_minutes} phut.";
}
