using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Application.Trips;

namespace SaigonWaterbus.Infrastructure.Data;

/// <summary>
/// Job nền: mỗi phút hủy chuyến sightseeing còn 5 phút tới giờ chạy nhưng chưa có khách đã xác nhận.
/// </summary>
public sealed class SightseeingTripAutoCancellationService : BackgroundService
{
    private static readonly TimeSpan ScanInterval = TimeSpan.FromMinutes(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SightseeingTripAutoCancellationService> _logger;
    private bool _databaseUnavailableWarningLogged;

    public SightseeingTripAutoCancellationService(
        IServiceScopeFactory scopeFactory,
        ILogger<SightseeingTripAutoCancellationService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(ScanInterval);

        await CancelEmptyTripsAsync(stoppingToken);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await CancelEmptyTripsAsync(stoppingToken);
        }
    }

    private async Task CancelEmptyTripsAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            if (!await dbContext.Database.CanConnectAsync(cancellationToken))
            {
                if (!_databaseUnavailableWarningLogged)
                {
                    _logger.LogWarning("Skipping sightseeing empty-trip cancellation scan because the database is not reachable.");
                    _databaseUnavailableWarningLogged = true;
                }

                return;
            }

            _databaseUnavailableWarningLogged = false;
            var context = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
            var timeProvider = scope.ServiceProvider.GetService<TimeProvider>() ?? TimeProvider.System;

            var cancelled = await SightseeingTripAutoCancellationSupport.CancelDueEmptySightseeingTripsAsync(
                context,
                timeProvider.GetUtcNow(),
                cancellationToken);

            if (cancelled > 0)
            {
                _logger.LogInformation("Cancelled {CancelledTripCount} empty sightseeing trips.", cancelled);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred while cancelling empty sightseeing trips.");
        }
    }
}
