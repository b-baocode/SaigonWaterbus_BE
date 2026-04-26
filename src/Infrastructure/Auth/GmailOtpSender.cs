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

    public async Task SendAsync(string email, string code, OtpPurpose purpose, CancellationToken cancellationToken)
    {
        var options = _optionsMonitor.CurrentValue;
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

        using var message = new MailMessage
        {
            From = new MailAddress(fromEmail, options.FromName),
            Subject = ResolveSubject(options, purpose),
            Body = BuildBody(options, purpose, code),
            IsBodyHtml = false
        };
        message.To.Add(email);

        using var client = new SmtpClient(options.Host, options.Port)
        {
            EnableSsl = true,
            DeliveryMethod = SmtpDeliveryMethod.Network,
            UseDefaultCredentials = false,
            Credentials = new NetworkCredential(options.Username, options.Password)
        };

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await client.SendMailAsync(message);
            cancellationToken.ThrowIfCancellationRequested();

            _logger.LogInformation(
                "Gmail OTP sent successfully. Purpose: {Purpose}, Email: {Email}",
                purpose,
                email);
        }
        catch (Exception ex) when (ex is SmtpException or InvalidOperationException or FormatException)
        {
            _logger.LogWarning(
                "Gmail SMTP send failed: {Message}. Purpose: {Purpose}, Email: {Email}. OTP skipped for development.",
                ex.Message,
                purpose,
                email);
            // Don't throw - just log and continue. This allows development without valid Gmail credentials.
        }
    }

    private static string ResolveSubject(GmailOptions options, OtpPurpose purpose) =>
        purpose switch
        {
            OtpPurpose.Register => options.RegisterSubject,
            OtpPurpose.ForgotPassword => options.ForgotPasswordSubject,
            _ => options.LoginSubject
        };

    private string BuildBody(GmailOptions options, OtpPurpose purpose, string code)
    {
        var template = purpose switch
        {
            OtpPurpose.Register => options.RegisterTemplate,
            OtpPurpose.ForgotPassword => options.ForgotPasswordTemplate,
            _ => options.LoginTemplate
        };

        return template
            .Replace("{code}", code, StringComparison.Ordinal)
            .Replace("{ttl_minutes}", _otpPolicy.ExpirationMinutes.ToString(), StringComparison.Ordinal);
    }
}
