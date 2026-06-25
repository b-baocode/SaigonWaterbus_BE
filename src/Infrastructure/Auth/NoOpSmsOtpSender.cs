using Microsoft.Extensions.Logging;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Infrastructure.Auth;

public sealed class NoOpSmsOtpSender : ISmsOtpSender
{
    private readonly ILogger<NoOpSmsOtpSender> _logger;

    public NoOpSmsOtpSender(ILogger<NoOpSmsOtpSender> logger)
    {
        _logger = logger;
    }

    public Task SendAsync(
        string phoneNumber,
        string code,
        OtpPurpose purpose,
        string? recipientName,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "SMS OTP send skipped (NoOp). Purpose: {Purpose}, PhoneNumber: {PhoneNumber}, RecipientName: {RecipientName}, Code: {Code}",
            purpose,
            phoneNumber,
            recipientName,
            code);

        return Task.CompletedTask;
    }
}
