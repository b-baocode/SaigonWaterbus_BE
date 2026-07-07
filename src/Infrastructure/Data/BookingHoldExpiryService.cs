using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SaigonWaterbus.Application.Bookings;
using SaigonWaterbus.Application.Common.Interfaces;

namespace SaigonWaterbus.Infrastructure.Data;

/// <summary>
/// Job nền: mỗi phút chuyển booking thường PendingPayment quá hạn giữ chỗ (15 phút)
/// sang Expired để nhả ghế, hoàn lượt khuyến mãi và phát sự kiện realtime.
/// </summary>
public sealed class BookingHoldExpiryService : BackgroundService
{
    private static readonly TimeSpan ScanInterval = TimeSpan.FromMinutes(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<BookingHoldExpiryService> _logger;
    private bool _databaseUnavailableWarningLogged;

    public BookingHoldExpiryService(
        IServiceScopeFactory scopeFactory,
        ILogger<BookingHoldExpiryService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(ScanInterval);

        await ExpireOverdueBookingsAsync(stoppingToken);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await ExpireOverdueBookingsAsync(stoppingToken);
        }
    }

    private async Task ExpireOverdueBookingsAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            if (!await dbContext.Database.CanConnectAsync(cancellationToken))
            {
                if (!_databaseUnavailableWarningLogged)
                {
                    _logger.LogWarning("Skipping booking hold expiry scan because the database is not reachable.");
                    _databaseUnavailableWarningLogged = true;
                }

                return;
            }

            _databaseUnavailableWarningLogged = false;
            var context = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
            var notifier = scope.ServiceProvider.GetService<ITripSeatNotifier>()
                ?? NullTripSeatNotifier.Instance;
            var timeProvider = scope.ServiceProvider.GetService<TimeProvider>() ?? TimeProvider.System;

            var expired = await BookingHoldExpirySupport.ExpireOverdueBookingsAsync(
                context,
                notifier,
                timeProvider.GetUtcNow(),
                cancellationToken);

            if (expired > 0)
            {
                _logger.LogInformation("Expired {ExpiredBookingCount} overdue pending-payment bookings.", expired);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred while expiring overdue pending-payment bookings.");
        }
    }
}
