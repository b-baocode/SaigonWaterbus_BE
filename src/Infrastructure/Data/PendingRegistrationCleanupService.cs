using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Infrastructure.Data;

public sealed class PendingRegistrationCleanupService : BackgroundService
{
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromMinutes(1);
    private const int BatchSize = 100;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<PendingRegistrationCleanupService> _logger;

    public PendingRegistrationCleanupService(
        IServiceScopeFactory scopeFactory,
        TimeProvider timeProvider,
        ILogger<PendingRegistrationCleanupService> logger)
    {
        _scopeFactory = scopeFactory;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await CleanupExpiredPendingRegistrationsAsync(stoppingToken);
            await Task.Delay(CleanupInterval, stoppingToken);
        }
    }

    private async Task CleanupExpiredPendingRegistrationsAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var now = _timeProvider.GetUtcNow();

            var expiredUsers = await context.Users
                .Where(x => x.Status == UserStatus.PendingVerification
                         && x.OtpChallenges.Any(otp => otp.Purpose == OtpPurpose.Register)
                         && !x.OtpChallenges.Any(otp => otp.Purpose == OtpPurpose.Register
                                                     && otp.ConsumedAt == null
                                                     && otp.ExpiresAt > now))
                .OrderBy(x => x.Id)
                .Take(BatchSize)
                .ToListAsync(cancellationToken);

            if (expiredUsers.Count == 0)
            {
                return;
            }

            context.Set<User>().RemoveRange(expiredUsers);
            await context.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Removed {Count} expired pending registration user(s).",
                expiredUsers.Count);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to cleanup expired pending registration users.");
        }
    }
}
