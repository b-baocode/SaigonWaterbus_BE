using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SaigonWaterbus.Domain.Enums;
using SaigonWaterbus.Infrastructure.Data;
using SaigonWaterbus.Infrastructure.Options;

namespace SaigonWaterbus.Infrastructure.Data;

/// <summary>
/// Background service that periodically advances trip status theo giờ thực tế:
///   - Scheduled/Boarding với departure_time <= now &lt; arrival_time  → InProgress (DB: 'Departed')
///   - Scheduled/Boarding/InProgress với arrival_time &lt;= now        → Completed   (DB: 'Arrived')
///
/// Kết hợp với trigger trg_sync_trip_status (chỉ chạy khi UPDATE time),
/// service này đảm bảo trip "đến giờ chạy" sẽ tự chuyển trạng thái mà
/// không cần admin sửa thủ công.
/// </summary>
public sealed class TripStatusAutoSyncService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _timeProvider;
    private readonly IOptions<TripStatusAutoSyncOptions> _options;
    private readonly ILogger<TripStatusAutoSyncService> _logger;

    public TripStatusAutoSyncService(
        IServiceScopeFactory scopeFactory,
        TimeProvider timeProvider,
        IOptions<TripStatusAutoSyncOptions> options,
        ILogger<TripStatusAutoSyncService> logger)
    {
        _scopeFactory = scopeFactory;
        _timeProvider = timeProvider;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Chạy ngay 1 lần khi khởi động để bắt kịp các trip đã qua giờ khi app restart
        await SyncTripStatusAsync(stoppingToken);

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

            await SyncTripStatusAsync(stoppingToken);
        }
    }

    private async Task SyncTripStatusAsync(CancellationToken cancellationToken)
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

            var nowUtc = _timeProvider.GetUtcNow();
            var nowUtcOffset = new DateTimeOffset(DateTime.SpecifyKind(nowUtc.UtcDateTime, DateTimeKind.Utc));

            // 1) Scheduled/Boarding/InProgress với arrival_time <= now → Completed
            //    (không đụng Cancelled)
            var arrivedQuery = dbContext.Trips
                .Where(t => t.TripStatus != TripStatus.Cancelled
                    && t.TripStatus != TripStatus.Completed
                    && t.ArrivalTime <= nowUtcOffset);

            var arrivedCount = 0;
            if (await arrivedQuery.AnyAsync(cancellationToken))
            {
                var arrivedList = await arrivedQuery.ToListAsync(cancellationToken);
                foreach (var trip in arrivedList)
                {
                    trip.TripStatus = TripStatus.Completed;
                    trip.LastStatusChangedAt = nowUtcOffset;
                    arrivedCount++;
                }
            }

            // 2) Scheduled/Boarding với departure_time <= now < arrival_time → InProgress
            //    (chỉ những trip chưa bị đánh dấu Completed ở bước 1)
            var departedQuery = dbContext.Trips
                .Where(t => t.TripStatus != TripStatus.Cancelled
                    && t.TripStatus != TripStatus.Completed
                    && t.TripStatus != TripStatus.InProgress
                    && t.DepartureTime <= nowUtcOffset
                    && t.ArrivalTime > nowUtcOffset);

            var departedCount = 0;
            if (await departedQuery.AnyAsync(cancellationToken))
            {
                var departedList = await departedQuery.ToListAsync(cancellationToken);
                foreach (var trip in departedList)
                {
                    trip.TripStatus = TripStatus.InProgress;
                    trip.LastStatusChangedAt = nowUtcOffset;
                    departedCount++;
                }
            }

            if (arrivedCount > 0 || departedCount > 0)
            {
                await dbContext.SaveChangesAsync(cancellationToken);
            }

            if (arrivedCount > 0 || departedCount > 0)
            {
                _logger.LogInformation(
                    "TripStatusAutoSync: {DepartedCount} trip → InProgress (Departed), {ArrivedCount} trip → Completed (Arrived) at {Now}.",
                    departedCount,
                    arrivedCount,
                    nowUtcOffset);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred while auto-syncing trip status.");
        }
    }
}