using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SaigonWaterbus.Application.CharterBookings;
using SaigonWaterbus.Infrastructure.Options;

namespace SaigonWaterbus.Infrastructure.Data;

public sealed class CharterBookingExpirationHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _timeProvider;
    private readonly IOptions<CharterBookingExpirationOptions> _options;
    private readonly ILogger<CharterBookingExpirationHostedService> _logger;
    private bool _databaseUnavailableWarningLogged;

    public CharterBookingExpirationHostedService(
        IServiceScopeFactory scopeFactory,
        TimeProvider timeProvider,
        IOptions<CharterBookingExpirationOptions> options,
        ILogger<CharterBookingExpirationHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _timeProvider = timeProvider;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await CleanupExpiredAsync(stoppingToken);

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

            await CleanupExpiredAsync(stoppingToken);
        }
    }

    private async Task CleanupExpiredAsync(CancellationToken cancellationToken)
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
                    _logger.LogWarning("Skipping charter booking expiration cleanup because the database is not reachable.");
                    _databaseUnavailableWarningLogged = true;
                }

                return;
            }

            _databaseUnavailableWarningLogged = false;
            var processor = scope.ServiceProvider.GetRequiredService<ICharterBookingExpirationProcessor>();
            var result = await processor.CleanupExpiredAsync(_timeProvider.GetUtcNow(), cancellationToken);
            if (result.ExpiredPayments > 0 || result.ExpiredCharterBookings > 0)
            {
                _logger.LogInformation(
                    "Expired {ExpiredPaymentCount} payment links and {ExpiredCharterBookingCount} charter bookings.",
                    result.ExpiredPayments,
                    result.ExpiredCharterBookings);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred while cleaning up expired charter booking holds and payment links.");
        }
    }
}
