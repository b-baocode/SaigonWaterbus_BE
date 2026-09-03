using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SaigonWaterbus.Application.Trips;
using SaigonWaterbus.Infrastructure.Options;

namespace SaigonWaterbus.Infrastructure.Data;

/// <summary>
/// Background service that periodically advances trip status theo giờ thực tế:
///   - Scheduled/Boarding với departure_time <= now &lt; arrival_time  → InProgress (DB: 'Departed')
///   - Scheduled/Boarding/InProgress với arrival_time &lt;= now        → Completed   (DB: 'Arrived')
///   - Trip Completed có booking nguồn còn Confirmed                 → booking Completed
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
            var nowUtc = _timeProvider.GetUtcNow();
            var nowUtcOffset = new DateTimeOffset(DateTime.SpecifyKind(nowUtc.UtcDateTime, DateTimeKind.Utc));
            var processor = scope.ServiceProvider.GetRequiredService<ITripStatusAutoSyncProcessor>();
            var result = await processor.SyncAsync(nowUtcOffset, cancellationToken);

            if (result.ArrivedTripCount > 0
                || result.DepartedTripCount > 0
                || result.CompletedBookingCount > 0)
            {
                _logger.LogInformation(
                    "TripStatusAutoSync: {DepartedCount} trip → InProgress (Departed), {ArrivedCount} trip → Completed (Arrived), {CompletedBookingCount} source booking → Completed at {Now}.",
                    result.DepartedTripCount,
                    result.ArrivedTripCount,
                    result.CompletedBookingCount,
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
