using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Application.Notifications;

namespace SaigonWaterbus.Infrastructure.Data;

/// <summary>
/// Job nền: mỗi phút quét chuyến khởi hành trong 60 phút tới và tạo in-app notification
/// nhắc giờ cho khách có booking Confirmed (mỗi user mỗi chuyến 1 lần).
/// </summary>
public sealed class TripReminderService : BackgroundService
{
    private static readonly TimeSpan ScanInterval = TimeSpan.FromMinutes(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TripReminderService> _logger;
    private bool _databaseUnavailableWarningLogged;

    public TripReminderService(
        IServiceScopeFactory scopeFactory,
        ILogger<TripReminderService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(ScanInterval);

        await SendDueRemindersAsync(stoppingToken);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await SendDueRemindersAsync(stoppingToken);
        }
    }

    private async Task SendDueRemindersAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            if (!await dbContext.Database.CanConnectAsync(cancellationToken))
            {
                if (!_databaseUnavailableWarningLogged)
                {
                    _logger.LogWarning("Skipping trip reminder scan because the database is not reachable.");
                    _databaseUnavailableWarningLogged = true;
                }

                return;
            }

            _databaseUnavailableWarningLogged = false;
            var context = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
            var timeProvider = scope.ServiceProvider.GetService<TimeProvider>() ?? TimeProvider.System;
            var realtimeNotifier = scope.ServiceProvider.GetService<INotificationRealtimeNotifier>()
                ?? NullNotificationRealtimeNotifier.Instance;

            var created = await TripReminderSupport.AddDueTripRemindersAsync(
                context,
                timeProvider.GetUtcNow(),
                cancellationToken,
                realtimeNotifier);

            if (created > 0)
            {
                _logger.LogInformation("Created {TripReminderCount} trip departure reminder notifications.", created);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred while creating trip departure reminders.");
        }
    }
}
