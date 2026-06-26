using Microsoft.Extensions.Options;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Enums;
using SaigonWaterbus.Infrastructure.Options;
using StackExchange.Redis;

namespace SaigonWaterbus.Infrastructure.Redis;

public sealed class RedisBoatHoldService : IBoatHoldService
{
    private readonly IConnectionMultiplexer _connectionMultiplexer;
    private readonly RedisOptions _options;

    public RedisBoatHoldService(IConnectionMultiplexer connectionMultiplexer, IOptions<RedisOptions> options)
    {
        _connectionMultiplexer = connectionMultiplexer;
        _options = options.Value;
    }

    public async Task<bool> TryHoldAsync(
        Guid bookingId,
        Guid boatId,
        DateOnly departureDate,
        TimeOnly? startTime,
        BoatRentalUnit rentalUnit,
        int durationValue,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken)
    {
        var ttl = expiresAt - DateTimeOffset.UtcNow;
        if (ttl <= TimeSpan.Zero)
        {
            return false;
        }

        var db = _connectionMultiplexer.GetDatabase();
        var key = BuildKey(boatId, departureDate, startTime, rentalUnit, durationValue);
        var owner = bookingId.ToString("N");

        var existing = await db.StringGetAsync(key).WaitAsync(cancellationToken);
        if (existing.HasValue)
        {
            return string.Equals(existing.ToString(), owner, StringComparison.OrdinalIgnoreCase);
        }

        return await db.StringSetAsync(key, owner, ttl, When.NotExists).WaitAsync(cancellationToken);
    }

    public async Task ReleaseAsync(
        Guid bookingId,
        Guid? boatId,
        DateOnly departureDate,
        TimeOnly? startTime,
        BoatRentalUnit rentalUnit,
        int durationValue,
        CancellationToken cancellationToken)
    {
        if (!boatId.HasValue)
        {
            return;
        }

        var db = _connectionMultiplexer.GetDatabase();
        var key = BuildKey(boatId.Value, departureDate, startTime, rentalUnit, durationValue);
        var owner = bookingId.ToString("N");
        var existing = await db.StringGetAsync(key).WaitAsync(cancellationToken);
        if (existing.HasValue && string.Equals(existing.ToString(), owner, StringComparison.OrdinalIgnoreCase))
        {
            await db.KeyDeleteAsync(key).WaitAsync(cancellationToken);
        }
    }

    private RedisKey BuildKey(
        Guid boatId,
        DateOnly departureDate,
        TimeOnly? startTime,
        BoatRentalUnit rentalUnit,
        int durationValue)
    {
        var timePart = startTime?.ToString("HHmm") ?? "day";
        return $"{_options.InstanceName}boat-hold:{boatId:N}:{departureDate:yyyyMMdd}:{timePart}:{rentalUnit}:{durationValue}";
    }
}
