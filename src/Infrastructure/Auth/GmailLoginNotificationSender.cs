using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SaigonWaterbus.Application.Common.Exceptions;
using SaigonWaterbus.Application.Common.Interfaces;

namespace SaigonWaterbus.Infrastructure.Auth;

public sealed class GmailLoginNotificationSender : ILoginNotificationSender
{
    private readonly IOptionsMonitor<GmailOptions> _gmailOptionsMonitor;
    private readonly IOptionsMonitor<LoginNotificationOptions> _notificationOptionsMonitor;
    private readonly ILogger<GmailLoginNotificationSender> _logger;

    public GmailLoginNotificationSender(
        IOptionsMonitor<GmailOptions> gmailOptionsMonitor,
        IOptionsMonitor<LoginNotificationOptions> notificationOptionsMonitor,
        ILogger<GmailLoginNotificationSender> logger)
    {
        _gmailOptionsMonitor = gmailOptionsMonitor;
        _notificationOptionsMonitor = notificationOptionsMonitor;
        _logger = logger;
    }

    public async Task SendLoginSucceededAsync(LoginNotification notification, CancellationToken cancellationToken)
    {
        var notificationOptions = _notificationOptionsMonitor.CurrentValue;
        var gmailOptions = _gmailOptionsMonitor.CurrentValue;

        if (!notificationOptions.Enabled || !gmailOptions.Enabled)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(gmailOptions.Username) || string.IsNullOrWhiteSpace(gmailOptions.Password))
        {
            _logger.LogWarning("Gmail login notification is enabled but Username/Password is missing.");
            throw new EmailDispatchException("Gmail Username/Password is not configured for login notification.");
        }

        var fromEmail = string.IsNullOrWhiteSpace(gmailOptions.FromEmail)
            ? gmailOptions.Username
            : gmailOptions.FromEmail;

        var recipientEmail = EmailRecipientResolver.Resolve(gmailOptions.TestRecipientEmail, notification.Email);
        using var message = new MailMessage
        {
            From = new MailAddress(fromEmail, gmailOptions.FromName),
            Subject = notificationOptions.Subject,
            Body = EmailRecipientResolver.AddOriginalRecipientNotice(
                BuildBody(notificationOptions, notification),
                isHtml: false,
                gmailOptions.TestRecipientEmail,
                notification.Email),
            IsBodyHtml = false
        };
        message.To.Add(recipientEmail);

        using var client = new SmtpClient(gmailOptions.Host, gmailOptions.Port)
        {
            EnableSsl = true,
            DeliveryMethod = SmtpDeliveryMethod.Network,
            UseDefaultCredentials = false,
            Credentials = new NetworkCredential(gmailOptions.Username, gmailOptions.Password),
            Timeout = 10000
        };

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await client.SendMailAsync(message);
            cancellationToken.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is SmtpException or InvalidOperationException or FormatException)
        {
            _logger.LogError(ex, "Gmail login notification failed for {Email}.", notification.Email);
            throw new EmailDispatchException($"Unable to send login notification via Gmail: {ex.Message}", ex);
        }
    }

    private static string BuildBody(LoginNotificationOptions options, LoginNotification notification) =>
        $"{options.WebName}: {notification.FullName} da dang nhap bang {notification.Provider} luc {notification.LoggedInAt:O}."
        + (string.IsNullOrWhiteSpace(notification.DeviceInfo) ? string.Empty : $" Thiet bi: {notification.DeviceInfo}");
}
