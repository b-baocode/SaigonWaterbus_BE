using Microsoft.Extensions.Logging;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Infrastructure.Auth;

public sealed class NoOpCustomBookingQuoteEmailSender : ICustomBookingQuoteEmailSender
{
    private readonly ILogger<NoOpCustomBookingQuoteEmailSender> _logger;

    public NoOpCustomBookingQuoteEmailSender(ILogger<NoOpCustomBookingQuoteEmailSender> logger)
    {
        _logger = logger;
    }

    public Task SendQuoteAsync(CustomBookingRequest request, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Custom booking quote email is disabled. Skipping email for request {CustomBookingRequestId}.",
            request.Id);
        return Task.CompletedTask;
    }
}
