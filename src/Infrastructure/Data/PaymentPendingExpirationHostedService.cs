using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Application.Payments;

namespace SaigonWaterbus.Infrastructure.Data;

public sealed class PaymentPendingExpirationHostedService : BackgroundService
{
    private static readonly TimeSpan ScanInterval = TimeSpan.FromMinutes(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PaymentPendingExpirationHostedService> _logger;
    private bool _databaseUnavailableWarningLogged;

    public PaymentPendingExpirationHostedService(
        IServiceScopeFactory scopeFactory,
        ILogger<PaymentPendingExpirationHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(ScanInterval);

        await CancelOverduePendingPaymentsAsync(stoppingToken);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await CancelOverduePendingPaymentsAsync(stoppingToken);
        }
    }

    private async Task CancelOverduePendingPaymentsAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            if (!await dbContext.Database.CanConnectAsync(cancellationToken))
            {
                if (!_databaseUnavailableWarningLogged)
                {
                    _logger.LogWarning("Skipping payment pending expiration scan because the database is not reachable.");
                    _databaseUnavailableWarningLogged = true;
                }

                return;
            }

            _databaseUnavailableWarningLogged = false;
            var context = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
            var timeProvider = scope.ServiceProvider.GetService<TimeProvider>() ?? TimeProvider.System;

            var cancelled = await PaymentPendingExpirationSupport.CancelOverduePendingPaymentsAsync(
                context,
                timeProvider.GetUtcNow(),
                cancellationToken);

            if (cancelled > 0)
            {
                _logger.LogInformation("Auto-cancelled {CancelledCount} overdue pending payments.", cancelled);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred while cancelling overdue pending payments.");
        }
    }
}
