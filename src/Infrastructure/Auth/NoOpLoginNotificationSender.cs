using Microsoft.Extensions.Logging;
using SaigonWaterbus.Application.Common.Interfaces;

namespace SaigonWaterbus.Infrastructure.Auth;

public sealed class NoOpLoginNotificationSender : ILoginNotificationSender
{
    private readonly ILogger<NoOpLoginNotificationSender> _logger;

    public NoOpLoginNotificationSender(ILogger<NoOpLoginNotificationSender> logger)
    {
        _logger = logger;
    }

    public Task SendLoginSucceededAsync(LoginNotification notification, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Login notification skipped. Email: {Email}, Provider: {Provider}",
            notification.Email,
            notification.Provider);

        return Task.CompletedTask;
    }
}
