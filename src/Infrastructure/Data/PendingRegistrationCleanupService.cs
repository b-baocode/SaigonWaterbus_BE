using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace SaigonWaterbus.Infrastructure.Data;

public sealed class PendingRegistrationCleanupService : BackgroundService
{
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromMinutes(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PendingRegistrationCleanupService> _logger;

    public PendingRegistrationCleanupService(
        IServiceScopeFactory scopeFactory,
        ILogger<PendingRegistrationCleanupService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(CleanupInterval);

        await CleanupExpiredPendingRegistrationsAsync(stoppingToken);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await CleanupExpiredPendingRegistrationsAsync(stoppingToken);
        }
    }

    private async Task CleanupExpiredPendingRegistrationsAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var initialiser = scope.ServiceProvider.GetRequiredService<ApplicationDbContextInitialiser>();
            var removed = await initialiser.CleanupExpiredPendingRegistrationUsersAsync(cancellationToken);

            if (removed > 0)
            {
                _logger.LogInformation("Removed {ExpiredPendingRegistrationCount} expired pending registration users.", removed);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred while cleaning up expired pending registration users.");
        }
    }
}
