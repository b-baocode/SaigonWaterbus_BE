using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SaigonWaterbus.Application.Common.Exceptions;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Infrastructure.Auth;

public sealed class GmailOtpSender : IOtpSender
{
    private readonly IOptionsMonitor<GmailOptions> _optionsMonitor;
    private readonly IOtpPolicy _otpPolicy;
    private readonly ILogger<GmailOtpSender> _logger;

    public GmailOtpSender(
        IOptionsMonitor<GmailOptions> optionsMonitor,
        IOtpPolicy otpPolicy,
        ILogger<GmailOtpSender> logger)
    {
        _optionsMonitor = optionsMonitor;
        _otpPolicy = otpPolicy;
        _logger = logger;
    }

    public async Task SendAsync(string email, string code, OtpPurpose purpose, string? recipientName, CancellationToken cancellationToken)
    {
        var options = _optionsMonitor.CurrentValue;
        var templateContent = OtpTemplateContentFactory.Create(purpose, email, recipientName);
        if (!options.Enabled)
        {
            _logger.LogWarning("Gmail OTP integration is disabled. Skipping OTP send to {Email}", email);
            return;
        }

        if (string.IsNullOrWhiteSpace(options.Username) || string.IsNullOrWhiteSpace(options.Password))
        {
            throw new OtpDispatchException("Gmail Username/Password is not configured.");
        }

        var fromEmail = string.IsNullOrWhiteSpace(options.FromEmail) ? options.Username : options.FromEmail;
        if (string.IsNullOrWhiteSpace(fromEmail))
        {
            throw new OtpDispatchException("Gmail FromEmail is not configured.");
        }

        var testRecipientEmail = options.EnableTestRecipientRedirect ? options.TestRecipientEmail : null;
        var recipientEmail = EmailRecipientResolver.Resolve(testRecipientEmail, email);
        var isHtml = IsHtmlTemplate(options, purpose);
        var body = EmailRecipientResolver.AddOriginalRecipientNotice(
            BuildBody(options, purpose, code, templateContent),
            isHtml,
            testRecipientEmail,
            email);

        using var message = new MailMessage
        {
            From = new MailAddress(fromEmail, options.FromName),
            Subject = ResolveSubject(options, purpose, templateContent),
            Body = body,
            IsBodyHtml = isHtml
        };
        message.To.Add(recipientEmail);

        using var client = new SmtpClient(options.Host, options.Port)
        {
            EnableSsl = true,
            DeliveryMethod = SmtpDeliveryMethod.Network,
            UseDefaultCredentials = false,
            Credentials = new NetworkCredential(options.Username, options.Password),
            Timeout = 10000
        };

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await client.SendMailAsync(message);
            cancellationToken.ThrowIfCancellationRequested();

            _logger.LogInformation(
                "Gmail OTP sent successfully. Purpose: {Purpose}, Email: {Email}, RecipientEmail: {RecipientEmail}",
                purpose,
                email,
                recipientEmail);
        }
        catch (Exception ex) when (ex is SmtpException or InvalidOperationException or FormatException)
        {
            _logger.LogError(
                ex,
                "Gmail SMTP send failed. Purpose: {Purpose}, Email: {Email}, RecipientEmail: {RecipientEmail}.",
                purpose,
                email,
                recipientEmail);
            throw new OtpDispatchException($"Unable to send OTP email via Gmail: {ex.Message}");
        }
    }

    private static string ResolveSubject(GmailOptions options, OtpPurpose purpose, OtpTemplateContent templateContent)
    {
        if (!string.IsNullOrWhiteSpace(options.Subject))
        {
            return options.Subject;
        }

        return purpose switch
        {
            OtpPurpose.Register => options.RegisterSubject,
            OtpPurpose.ForgotPassword => options.ForgotPasswordSubject,
            OtpPurpose.EmailChange or OtpPurpose.PhoneChange => options.EmailChangeSubject,
            _ => options.LoginSubject
        };
    }

    private string BuildBody(GmailOptions options, OtpPurpose purpose, string code, OtpTemplateContent templateContent)
    {
        var template = ResolveTemplate(options, purpose);

        return template
            .Replace("{title}", templateContent.Title, StringComparison.Ordinal)
            .Replace("{message}", templateContent.Message, StringComparison.Ordinal)
            .Replace("{code}", code, StringComparison.Ordinal)
            .Replace("{name}", templateContent.Username, StringComparison.Ordinal)
            .Replace("{username}", templateContent.Username, StringComparison.Ordinal)
            .Replace("{ttl_minutes}", _otpPolicy.ExpirationMinutes.ToString(), StringComparison.Ordinal);
    }

    private static string ResolveTemplate(GmailOptions options, OtpPurpose purpose)
    {
        if (!string.IsNullOrWhiteSpace(options.Template))
        {
            return options.Template;
        }

        return purpose switch
        {
            OtpPurpose.Register => options.RegisterTemplate,
            OtpPurpose.ForgotPassword => options.ForgotPasswordTemplate,
            OtpPurpose.EmailChange or OtpPurpose.PhoneChange => options.EmailChangeTemplate,
            _ => options.LoginTemplate
        };
    }

    private static bool IsHtmlTemplate(GmailOptions options, OtpPurpose purpose)
    {
        var template = ResolveTemplate(options, purpose);

        return template.Contains("<html", StringComparison.OrdinalIgnoreCase)
            || template.Contains("<body", StringComparison.OrdinalIgnoreCase)
            || template.Contains("<table", StringComparison.OrdinalIgnoreCase);
    }
}
