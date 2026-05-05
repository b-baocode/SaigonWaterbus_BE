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
    public string Subject { get; set; } = string.Empty;
    public string Template { get; set; } = string.Empty;
    public string RegisterSubject { get; set; } = "Mã OTP đăng ký Saigon Waterbus";
    public string LoginSubject { get; set; } = "Mã OTP đăng nhập Saigon Waterbus";
    public string ForgotPasswordSubject { get; set; } = "Mã OTP quên mật khẩu Saigon Waterbus";
    public string EmailChangeSubject { get; set; } = "Mã OTP xác thực email mới Saigon Waterbus";
    public string RegisterTemplate { get; set; } =
        "Mã OTP đăng ký Saigon Waterbus của bạn là {code}. Hiệu lực {ttl_minutes} phút.";
    public string LoginTemplate { get; set; } =
        "Mã OTP đăng nhập Saigon Waterbus của bạn là {code}. Hiệu lực {ttl_minutes} phút.";
    public string ForgotPasswordTemplate { get; set; } =
        "Mã OTP quên mật khẩu Saigon Waterbus của bạn là {code}. Hiệu lực {ttl_minutes} phút.";
    public string EmailChangeTemplate { get; set; } =
        "Mã OTP xác thực email mới Saigon Waterbus của bạn là {code}. Hiệu lực {ttl_minutes} phút.";
}
