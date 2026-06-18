using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SaigonWaterbus.Application.TripSchedules;
using SaigonWaterbus.Infrastructure.Data;

namespace SaigonWaterbus.Infrastructure.Data;

public sealed class TripGenerationBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TripGenerationBackgroundService> _logger;

    public TripGenerationBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<TripGenerationBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Chạy ngay khi app khởi động để fill gaps nếu có
        await RunGenerationAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            // Sleep đến 00:05 ngày mai (giờ UTC+7)
            var delay = ComputeDelayUntilNextRun();
            _logger.LogInformation("TripGeneration: next run in {Delay}", delay);

            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            await RunGenerationAsync(stoppingToken);
        }
    }

    private async Task RunGenerationAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            if (!await db.Database.CanConnectAsync(cancellationToken))
            {
                _logger.LogWarning("TripGeneration skipped: database not reachable.");
                return;
            }

            var sender = scope.ServiceProvider.GetRequiredService<ISender>();
            var result = await sender.Send(new GenerateScheduledTripsCommand(), cancellationToken);

            _logger.LogInformation(
                "TripGeneration completed: {Created} trips created across {Schedules} schedules.",
                result.TripsCreated, result.SchedulesProcessed);

            foreach (var error in result.Errors)
                _logger.LogWarning("TripGeneration error: {Error}", error);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "TripGeneration: unexpected error during generation.");
        }
    }

    // Tính thời gian delay đến 00:05 ngày hôm sau theo UTC+7
    private static TimeSpan ComputeDelayUntilNextRun()
    {
        var vnNow = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(7));
        var nextRun = vnNow.Date.AddDays(1).Add(TimeSpan.FromMinutes(5));
        var nextRunOffset = new DateTimeOffset(nextRun, TimeSpan.FromHours(7));
        var delay = nextRunOffset - vnNow;
        return delay > TimeSpan.Zero ? delay : TimeSpan.FromHours(24);
    }
}
