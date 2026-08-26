using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SaigonWaterbus.Application.CharterBookings;
using SaigonWaterbus.Infrastructure.Options;

namespace SaigonWaterbus.Infrastructure.Data;

public sealed class CharterBookingTicketReconciliationHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _timeProvider;
    private readonly IOptions<CharterBookingTicketReconciliationOptions> _options;
    private readonly ILogger<CharterBookingTicketReconciliationHostedService> _logger;
    private bool _databaseUnavailableWarningLogged;

    public CharterBookingTicketReconciliationHostedService(
        IServiceScopeFactory scopeFactory,
        TimeProvider timeProvider,
        IOptions<CharterBookingTicketReconciliationOptions> options,
        ILogger<CharterBookingTicketReconciliationHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _timeProvider = timeProvider;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await ReconcileAsync(stoppingToken);

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

            await ReconcileAsync(stoppingToken);
        }
    }

    private async Task ReconcileAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (!_options.Value.Enabled)
            {
                return;
            }

            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            if (!await dbContext.Database.CanConnectAsync(cancellationToken))
            {
                if (!_databaseUnavailableWarningLogged)
                {
                    _logger.LogWarning("Skipping charter ticket reconciliation because the database is not reachable.");
                    _databaseUnavailableWarningLogged = true;
                }

                return;
            }

            _databaseUnavailableWarningLogged = false;
            var processor = scope.ServiceProvider.GetRequiredService<ICharterBookingTicketReconciliationProcessor>();
            var result = await processor.ReconcileAsync(cancellationToken);
            if (result.ReconciledBookingCount > 0)
            {
                _logger.LogInformation(
                    "Auto-issued {IssuedTicketCount} missing charter tickets across {BookingCount} paid bookings.",
                    result.IssuedTicketCount,
                    result.ReconciledBookingCount);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred while reconciling missing charter tickets.");
        }
    }
}
