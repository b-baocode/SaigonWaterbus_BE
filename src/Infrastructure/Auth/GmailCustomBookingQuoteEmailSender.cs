using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Infrastructure.Auth;

public sealed class GmailCustomBookingQuoteEmailSender : ICustomBookingQuoteEmailSender
{
    private readonly IOptionsMonitor<GmailOptions> _optionsMonitor;
    private readonly ILogger<GmailCustomBookingQuoteEmailSender> _logger;

    public GmailCustomBookingQuoteEmailSender(
        IOptionsMonitor<GmailOptions> optionsMonitor,
        ILogger<GmailCustomBookingQuoteEmailSender> logger)
    {
        _optionsMonitor = optionsMonitor;
        _logger = logger;
    }

    public async Task SendQuoteAsync(CustomBookingRequest request, CancellationToken cancellationToken)
    {
        var options = _optionsMonitor.CurrentValue;
        if (!options.Enabled)
        {
            _logger.LogInformation("Gmail is disabled. Skipping custom booking quote email for {RequestId}.", request.Id);
            return;
        }

        if (string.IsNullOrWhiteSpace(request.ContactEmail))
        {
            _logger.LogInformation("Custom booking request {RequestId} has no contact email. Skipping quote email.", request.Id);
            return;
        }

        if (string.IsNullOrWhiteSpace(options.Username) || string.IsNullOrWhiteSpace(options.Password))
        {
            _logger.LogWarning("Gmail Username/Password is not configured. Skipping custom booking quote email for {RequestId}.", request.Id);
            return;
        }

        var fromEmail = string.IsNullOrWhiteSpace(options.FromEmail) ? options.Username : options.FromEmail;
        if (string.IsNullOrWhiteSpace(fromEmail))
        {
            _logger.LogWarning("Gmail FromEmail is not configured. Skipping custom booking quote email for {RequestId}.", request.Id);
            return;
        }

        using var message = new MailMessage
        {
            From = new MailAddress(fromEmail, options.FromName),
            Subject = CustomBookingQuoteEmailContentFactory.Subject(request),
            Body = CustomBookingQuoteEmailContentFactory.PlainText(request),
            IsBodyHtml = false
        };
        message.To.Add(request.ContactEmail);

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
            _logger.LogInformation("Custom booking quote email sent by Gmail. RequestId: {RequestId}, Email: {Email}", request.Id, request.ContactEmail);
        }
        catch (Exception ex) when (ex is SmtpException or InvalidOperationException or FormatException)
        {
            _logger.LogWarning(ex, "Gmail custom booking quote email failed. RequestId: {RequestId}, Email: {Email}", request.Id, request.ContactEmail);
        }
    }
}
