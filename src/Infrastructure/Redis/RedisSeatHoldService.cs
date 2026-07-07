using Microsoft.Extensions.Options;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Infrastructure.Options;
using StackExchange.Redis;

namespace SaigonWaterbus.Infrastructure.Redis;

/// <summary>
/// Giữ ghế tạm thời bằng Redis: mỗi ghế một key với TTL, tự hết hạn không cần job dọn.
/// Key: {instance}seat-hold:{tripId}:{tripSeatId} → userId.
/// </summary>
public sealed class RedisSeatHoldService : ISeatHoldService
{
    private readonly IConnectionMultiplexer _connectionMultiplexer;
    private readonly RedisOptions _options;

    public RedisSeatHoldService(IConnectionMultiplexer connectionMultiplexer, IOptions<RedisOptions> options)
    {
        _connectionMultiplexer = connectionMultiplexer;
        _options = options.Value;
    }

    public async Task<IReadOnlyList<Guid>> TryHoldAsync(
        Guid tripId,
        IReadOnlyList<Guid> tripSeatIds,
        Guid userId,
        TimeSpan ttl,
        CancellationToken cancellationToken)
    {
        if (ttl <= TimeSpan.Zero)
        {
            return tripSeatIds;
        }

        var db = _connectionMultiplexer.GetDatabase();
        var owner = userId.ToString("N");
        var failed = new List<Guid>();

        foreach (var tripSeatId in tripSeatIds)
        {
            var key = BuildKey(tripId, tripSeatId);
            if (await db.StringSetAsync(key, owner, ttl, When.NotExists).WaitAsync(cancellationToken))
            {
                continue;
            }

            var existing = await db.StringGetAsync(key).WaitAsync(cancellationToken);
            if (existing.HasValue && string.Equals(existing.ToString(), owner, StringComparison.OrdinalIgnoreCase))
            {
                // Ghế đã do chính user giữ → gia hạn TTL.
                await db.KeyExpireAsync(key, ttl).WaitAsync(cancellationToken);
                continue;
            }

            failed.Add(tripSeatId);
        }

        return failed;
    }

    public async Task ReleaseAsync(
        Guid tripId,
        IReadOnlyList<Guid> tripSeatIds,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var db = _connectionMultiplexer.GetDatabase();
        var owner = userId.ToString("N");

        foreach (var tripSeatId in tripSeatIds)
        {
            var key = BuildKey(tripId, tripSeatId);
            var existing = await db.StringGetAsync(key).WaitAsync(cancellationToken);
            if (existing.HasValue && string.Equals(existing.ToString(), owner, StringComparison.OrdinalIgnoreCase))
            {
                await db.KeyDeleteAsync(key).WaitAsync(cancellationToken);
            }
        }
    }

    public async Task<IReadOnlyDictionary<Guid, Guid>> GetHeldSeatsAsync(
        Guid tripId,
        CancellationToken cancellationToken)
    {
        var db = _connectionMultiplexer.GetDatabase();
        var pattern = $"{_options.InstanceName}seat-hold:{tripId:N}:*";
        var prefixLength = $"{_options.InstanceName}seat-hold:{tripId:N}:".Length;
        var result = new Dictionary<Guid, Guid>();

        foreach (var endpoint in _connectionMultiplexer.GetEndPoints())
        {
            var server = _connectionMultiplexer.GetServer(endpoint);
            if (!server.IsConnected || server.IsReplica)
            {
                continue;
            }

            await foreach (var key in server.KeysAsync(pattern: pattern, pageSize: 250)
                .WithCancellation(cancellationToken))
            {
                var keyText = key.ToString();
                if (keyText.Length <= prefixLength
                    || !Guid.TryParse(keyText[prefixLength..], out var tripSeatId))
                {
                    continue;
                }

                var value = await db.StringGetAsync(key).WaitAsync(cancellationToken);
                if (value.HasValue && Guid.TryParse(value.ToString(), out var holderUserId))
                {
                    result[tripSeatId] = holderUserId;
                }
            }
        }

        return result;
    }

    private RedisKey BuildKey(Guid tripId, Guid tripSeatId) =>
        $"{_options.InstanceName}seat-hold:{tripId:N}:{tripSeatId:N}";
}
