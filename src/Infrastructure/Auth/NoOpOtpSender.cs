using Microsoft.Extensions.Logging;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Infrastructure.Auth;

/// <summary>
/// A no-op OTP sender that does nothing. Used for development/testing.
/// </summary>
public sealed class NoOpOtpSender : IOtpSender
{
    private readonly ILogger<NoOpOtpSender> _logger;

    public NoOpOtpSender(ILogger<NoOpOtpSender> logger)
    {
        _logger = logger;
    }

    public Task SendAsync(string email, string code, OtpPurpose purpose, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "OTP send skipped (NoOp). Purpose: {Purpose}, Email: {Email}, Code: {Code}",
            purpose,
            email,
            code);
        return Task.CompletedTask;
    }
}
