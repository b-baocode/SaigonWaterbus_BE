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
                "Xac nhan dang ky",
                "Nhap ma OTP de hoan tat dang ky tai khoan Saigon Waterbus.",
                username),
            OtpPurpose.ForgotPassword => new OtpTemplateContent(
                "Dat lai mat khau",
                "Nhap ma OTP de dat lai mat khau tai khoan Saigon Waterbus.",
                username),
            _ => new OtpTemplateContent(
                "Xac thuc dang nhap",
                "Nhap ma OTP de tiep tuc dang nhap vao he thong Saigon Waterbus.",
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
