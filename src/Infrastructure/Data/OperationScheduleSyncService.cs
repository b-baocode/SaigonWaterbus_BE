using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Infrastructure.Options;

namespace SaigonWaterbus.Infrastructure.Data;

public sealed class OperationScheduleSyncService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _timeProvider;
    private readonly IOptions<OperationScheduleSyncOptions> _options;
    private readonly ILogger<OperationScheduleSyncService> _logger;
    private bool _databaseUnavailableWarningLogged;

    public OperationScheduleSyncService(
        IServiceScopeFactory scopeFactory,
        TimeProvider timeProvider,
        IOptions<OperationScheduleSyncOptions> options,
        ILogger<OperationScheduleSyncService> logger)
    {
        _scopeFactory = scopeFactory;
        _timeProvider = timeProvider;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await SyncOperationScheduleAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            var interval = TimeSpan.FromSeconds(Math.Max(5, _options.Value.IntervalSeconds));
            using var timer = new PeriodicTimer(interval, _timeProvider);

            try
            {
                if (!await timer.WaitForNextTickAsync(stoppingToken))
                {
                    break;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            await SyncOperationScheduleAsync(stoppingToken);
        }
    }

    private async Task SyncOperationScheduleAsync(CancellationToken cancellationToken)
    {
        try
        {
            var options = _options.Value;
            if (!options.Enabled)
            {
                return;
            }

            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            if (!await dbContext.Database.CanConnectAsync(cancellationToken))
            {
                if (!_databaseUnavailableWarningLogged)
                {
                    _logger.LogWarning("Skipping operation schedule sync because the database is not reachable.");
                    _databaseUnavailableWarningLogged = true;
                }

                return;
            }

            _databaseUnavailableWarningLogged = false;
            var now = _timeProvider.GetUtcNow();
            var from = now.AddDays(-Math.Max(0, options.PastDays));
            var to = now.AddDays(Math.Max(1, options.HorizonDays));
            var synchronizer = scope.ServiceProvider.GetRequiredService<IOperationScheduleSynchronizer>();
            var count = await synchronizer.SyncAsync(from, to, cancellationToken);

            _logger.LogInformation(
                "Synced {OperationScheduleSourceCount} operation schedule sources between {From} and {To}.",
                count,
                from,
                to);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred while syncing operation schedule entries.");
        }
    }
}
