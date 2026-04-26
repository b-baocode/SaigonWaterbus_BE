using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Options;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Infrastructure.Options;

namespace SaigonWaterbus.Infrastructure.Notifications;

public class GmailRegistrationNotificationService : IRegistrationNotificationService
{
    private readonly EmailSettings _settings;

    public GmailRegistrationNotificationService(IOptions<EmailSettings> settings)
    {
        _settings = settings.Value;
    }

    public async Task SendRegistrationCreatedAsync(
        string recipientEmail,
        string recipientName,
        string roleName,
        CancellationToken cancellationToken)
    {
        ValidateSettings();

        using var message = new MailMessage
        {
            From = new MailAddress(_settings.SenderEmail, _settings.SenderName),
            Subject = "Saigon Waterbus - Registration Successful",
            Body =
                $"Hello {recipientName},\n\n" +
                $"Your account has been created successfully.\n" +
                $"Assigned role: {roleName}\n" +
                $"Registered email: {recipientEmail}\n\n" +
                "Saigon Waterbus",
            IsBodyHtml = false
        };

        message.To.Add(recipientEmail);

        using var client = new SmtpClient(_settings.SmtpHost, _settings.SmtpPort)
        {
            EnableSsl = _settings.EnableSsl,
            Credentials = new NetworkCredential(_settings.SenderEmail, _settings.AppPassword)
        };

        cancellationToken.ThrowIfCancellationRequested();
        await client.SendMailAsync(message, cancellationToken);
    }

    private void ValidateSettings()
    {
        if (string.IsNullOrWhiteSpace(_settings.SenderEmail) ||
            string.IsNullOrWhiteSpace(_settings.AppPassword))
        {
            throw new InvalidOperationException(
                "Email sender settings are missing. Configure Email:SenderEmail and Email:AppPassword.");
        }
    }
}
