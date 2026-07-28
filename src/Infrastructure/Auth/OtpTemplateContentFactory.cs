using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Infrastructure.Auth;

internal sealed record OtpTemplateContent(string Title, string Message, string Username);

internal static class OtpTemplateContentFactory
{
    public static OtpTemplateContent Create(OtpPurpose purpose, string email, string? recipientName)
    {
        var username = ResolveUsername(email, recipientName);

        return purpose switch
        {
            OtpPurpose.Register => new OtpTemplateContent(
                "Xác nhận đăng ký",
                "Nhập mã OTP để hoàn tất đăng ký tài khoản Waterbus.",
                username),
            OtpPurpose.ForgotPassword => new OtpTemplateContent(
                "Đặt lại mật khẩu",
                "Nhập mã OTP để đặt lại mật khẩu tài khoản Waterbus.",
                username),
            OtpPurpose.EmailChange => new OtpTemplateContent(
                "Xác thực email mới",
                "Nhập mã OTP để xác thực email mới cho tài khoản Waterbus.",
                username),
            OtpPurpose.PhoneChange => new OtpTemplateContent(
                "Xác thực số điện thoại",
                "Nhập mã OTP để xác thực số điện thoại cho tài khoản Waterbus.",
                username),
            OtpPurpose.Refund => new OtpTemplateContent(
                "Xác thực hoàn tiền",
                "Nhập mã OTP để xác nhận yêu cầu hoàn tiền Waterbus.",
                username),
            _ => new OtpTemplateContent(
                "Xác thực đăng nhập",
                "Nhập mã OTP để tiếp tục đăng nhập vào hệ thống Waterbus.",
                username)
        };
    }

    private static string ResolveUsername(string email, string? recipientName)
    {
        if (!string.IsNullOrWhiteSpace(recipientName))
        {
            return recipientName.Trim();
        }

        var atIndex = email.IndexOf('@');
        if (atIndex > 0)
        {
            return email[..atIndex];
        }

        return email.Trim();
    }
}
