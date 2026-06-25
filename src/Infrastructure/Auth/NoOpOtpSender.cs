using Microsoft.Extensions.Logging;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Infrastructure.Auth;

public sealed class NoOpOtpSender : IOtpSender
{
    private readonly ILogger<NoOpOtpSender> _logger;

    public NoOpOtpSender(ILogger<NoOpOtpSender> logger)
    {
        _logger = logger;
    }

    public Task SendAsync(string email, string code, OtpPurpose purpose, string? recipientName, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "OTP send skipped (NoOp). Purpose: {Purpose}, Email: {Email}, RecipientName: {RecipientName}, Code: {Code}",
            purpose,
            email,
            recipientName,
            code);
        return Task.CompletedTask;
    }
}
