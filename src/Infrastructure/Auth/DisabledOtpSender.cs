using Microsoft.Extensions.Logging;
using SaigonWaterbus.Application.Common.Exceptions;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Infrastructure.Auth;

public sealed class DisabledOtpSender : IOtpSender
{
    private readonly ILogger<DisabledOtpSender> _logger;

    public DisabledOtpSender(ILogger<DisabledOtpSender> logger)
    {
        _logger = logger;
    }

    public Task SendAsync(string email, string code, OtpPurpose purpose, string? recipientName, CancellationToken cancellationToken)
    {
        _logger.LogError(
            "OTP email provider is not configured. Purpose: {Purpose}, Email: {Email}",
            purpose,
            email);

        throw new OtpDispatchException("OTP email provider is not configured. Enable Brevo or Gmail before using email OTP.");
    }
}
