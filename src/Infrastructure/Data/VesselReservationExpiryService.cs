using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SaigonWaterbus.Application.CustomBookingRequests;

namespace SaigonWaterbus.Infrastructure.Data;

public sealed class VesselReservationExpiryService : BackgroundService
{
    private static readonly TimeSpan ExpiryInterval = TimeSpan.FromMinutes(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<VesselReservationExpiryService> _logger;
    private bool _databaseUnavailableWarningLogged;

    public VesselReservationExpiryService(
        IServiceScopeFactory scopeFactory,
        TimeProvider timeProvider,
        ILogger<VesselReservationExpiryService> logger)
    {
        _scopeFactory = scopeFactory;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(ExpiryInterval);

        await ExpireReservationsAsync(stoppingToken);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await ExpireReservationsAsync(stoppingToken);
        }
    }

    private async Task ExpireReservationsAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            if (!await dbContext.Database.CanConnectAsync(cancellationToken))
            {
                if (!_databaseUnavailableWarningLogged)
                {
                    _logger.LogWarning("Skipping vessel reservation expiry because the database is not reachable.");
                    _databaseUnavailableWarningLogged = true;
                }

                return;
            }

            _databaseUnavailableWarningLogged = false;
            var now = _timeProvider.GetUtcNow();
            var expired = await CustomBookingVesselReservations.ExpireStaleReservationsAsync(
                dbContext,
                now,
                cancellationToken);
            if (expired > 0)
            {
                await dbContext.SaveChangesAsync(cancellationToken);
                _logger.LogInformation("Expired stale vessel reservations at {Now}.", now);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred while expiring vessel reservations.");
        }
    }
}
