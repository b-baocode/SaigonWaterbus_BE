using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Application.Trips;

namespace SaigonWaterbus.Infrastructure.Data;

public sealed class CharterTripExpirationHostedService : BackgroundService
{
    private static readonly TimeSpan ScanInterval = TimeSpan.FromMinutes(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<CharterTripExpirationHostedService> _logger;
    private bool _databaseUnavailableWarningLogged;

    public CharterTripExpirationHostedService(
        IServiceScopeFactory scopeFactory,
        ILogger<CharterTripExpirationHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(ScanInterval);

        await CompleteAndDeleteOverdueTripsAsync(stoppingToken);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await CompleteAndDeleteOverdueTripsAsync(stoppingToken);
        }
    }

    private async Task CompleteAndDeleteOverdueTripsAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            if (!await dbContext.Database.CanConnectAsync(cancellationToken))
            {
                if (!_databaseUnavailableWarningLogged)
                {
                    _logger.LogWarning("Skipping charter trip expiration scan because the database is not reachable.");
                    _databaseUnavailableWarningLogged = true;
                }

                return;
            }

            _databaseUnavailableWarningLogged = false;
            var context = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
            var timeProvider = scope.ServiceProvider.GetService<TimeProvider>() ?? TimeProvider.System;

            var (completed, deleted) = await CharterTripExpirationSupport.CompleteAndDeleteOverdueCharterTripsAsync(
                context,
                timeProvider.GetUtcNow(),
                cancellationToken);

            if (completed > 0 || deleted > 0)
            {
                _logger.LogInformation(
                    "Auto-completed {CompletedCount} overdue charter trips and deleted {DeletedCount} terminal trips.",
                    completed,
                    deleted);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred while completing/deleting overdue charter trips.");
        }
    }
}
