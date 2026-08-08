using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Application.Notifications;

namespace SaigonWaterbus.Infrastructure.Data;

/// <summary>
/// Quét các mốc gần đến của ca Staff OnBoard và chuyến theo tàu để tạo notification
/// nếu không có request nào khác làm phát sinh event trạng thái.
/// </summary>
public sealed class StaffOperationalNotificationService : BackgroundService
{
    private static readonly TimeSpan ScanInterval = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan LookBack = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan LookAhead = TimeSpan.FromMinutes(30);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<StaffOperationalNotificationService> _logger;

    public StaffOperationalNotificationService(
        IServiceScopeFactory scopeFactory,
        ILogger<StaffOperationalNotificationService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(ScanInterval);
        await ScanAsync(stoppingToken);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await ScanAsync(stoppingToken);
        }
    }

    private async Task ScanAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            if (!await dbContext.Database.CanConnectAsync(cancellationToken))
            {
                return;
            }

            var context = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
            var timeProvider = scope.ServiceProvider.GetService<TimeProvider>() ?? TimeProvider.System;
            var realtimeNotifier = scope.ServiceProvider.GetService<INotificationRealtimeNotifier>()
                ?? NullNotificationRealtimeNotifier.Instance;
            var created = await StaffTripNotificationSupport.AddDueOperationalNotificationsAsync(
                context,
                timeProvider.GetUtcNow(),
                LookBack,
                LookAhead,
                cancellationToken);

            if (created.Count == 0)
            {
                return;
            }

            await context.SaveChangesAsync(cancellationToken);
            await NotificationSupport.PublishCreatedAsync(
                realtimeNotifier,
                created,
                cancellationToken);
            _logger.LogInformation(
                "Created {StaffOperationalNotificationCount} staff operational notifications.",
                created.Count);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred while creating staff operational notifications.");
        }
    }
}
