using Microsoft.Extensions.Logging;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Infrastructure.Auth;

public sealed class NoOpCustomBookingConfirmationEmailSender : ICustomBookingConfirmationEmailSender
{
    private readonly ILogger<NoOpCustomBookingConfirmationEmailSender> _logger;

    public NoOpCustomBookingConfirmationEmailSender(ILogger<NoOpCustomBookingConfirmationEmailSender> logger)
    {
        _logger = logger;
    }

    public Task SendConfirmationAsync(
        CustomBookingRequest request,
        string? qrPayload,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Custom booking confirmation email is disabled. Skipping email for request {CustomBookingRequestId}.",
            request.Id);
        return Task.CompletedTask;
    }
}
