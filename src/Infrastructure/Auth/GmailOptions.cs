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
    public string FromName { get; set; } = "Waterbus";
    /// <summary>Base URL public của API để dựng link ảnh QR trong email (vd https://api.example.com).</summary>
    public string? PublicApiBaseUrl { get; set; }
    /// <summary>
    /// Cho phép chuyển hướng email sang TestRecipientEmail. Mặc định tắt để production luôn gửi
    /// đúng địa chỉ của khách, kể cả khi còn sót TestRecipientEmail trong cấu hình triển khai.
    /// </summary>
    public bool EnableTestRecipientRedirect { get; set; }

    public string? TestRecipientEmail { get; set; }
    public string Subject { get; set; } = string.Empty;
    public string Template { get; set; } = string.Empty;
    public string RegisterSubject { get; set; } = "Mã OTP đăng ký Waterbus";
    public string LoginSubject { get; set; } = "Mã OTP đăng nhập Waterbus";
    public string ForgotPasswordSubject { get; set; } = "Mã OTP quên mật khẩu Waterbus";
    public string EmailChangeSubject { get; set; } = "Mã OTP xác thực email mới Waterbus";
    public string RegisterTemplate { get; set; } =
        "Mã OTP đăng ký Waterbus của bạn là {code}. Hiệu lực {ttl_minutes} phút.";
    public string LoginTemplate { get; set; } =
        "Mã OTP đăng nhập Waterbus của bạn là {code}. Hiệu lực {ttl_minutes} phút.";
    public string ForgotPasswordTemplate { get; set; } =
        "Mã OTP quên mật khẩu Waterbus của bạn là {code}. Hiệu lực {ttl_minutes} phút.";
    public string EmailChangeTemplate { get; set; } =
        "Mã OTP xác thực email mới Waterbus của bạn là {code}. Hiệu lực {ttl_minutes} phút.";
}
