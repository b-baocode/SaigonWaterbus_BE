using Microsoft.Extensions.Options;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Infrastructure.Options;
using StackExchange.Redis;

namespace SaigonWaterbus.Infrastructure.Redis;

/// <summary>
/// Giữ ghế tạm thời bằng Redis theo chặng. Mỗi ghế một HASH:
/// key {instance}seat-hold:{tripId}:{tripSeatId}, field "{fromOrder}:{toOrder}" → "{userId}:{expiresAtUnixMs}".
/// Check giao chặng + ghi lượt giữ chạy trong một Lua script nên atomic trên từng ghế;
/// TTL của key luôn ≥ lượt giữ xa nhất nên key tự hết hạn không cần job dọn.
/// </summary>
public sealed class RedisSeatHoldService : ISeatHoldService
{
    private readonly IConnectionMultiplexer _connectionMultiplexer;
    private readonly RedisOptions _options;

    // ARGV: [1]=nowUnixMs, [2]=owner, [3]=fromOrder, [4]=toOrder, [5]=ttlMs
    // Trả 1 nếu giữ được, 0 nếu có user khác giữ chặng giao nhau.
    private const string TryHoldScript = """
        local now = tonumber(ARGV[1])
        local owner = ARGV[2]
        local from = tonumber(ARGV[3])
        local to = tonumber(ARGV[4])
        local ttlms = tonumber(ARGV[5])
        local fields = redis.call('HGETALL', KEYS[1])
        for i = 1, #fields, 2 do
            local f = fields[i]
            local v = fields[i + 1]
            local fsep = string.find(f, ':')
            local hFrom = tonumber(string.sub(f, 1, fsep - 1))
            local hTo = tonumber(string.sub(f, fsep + 1))
            local vsep = string.find(v, ':')
            local hOwner = string.sub(v, 1, vsep - 1)
            local hExp = tonumber(string.sub(v, vsep + 1))
            if hExp <= now then
                redis.call('HDEL', KEYS[1], f)
            elseif hOwner ~= owner and hFrom < to and from < hTo then
                return 0
            end
        end
        redis.call('HSET', KEYS[1], from .. ':' .. to, owner .. ':' .. (now + ttlms))
        local remaining = redis.call('PTTL', KEYS[1])
        if remaining < ttlms then
            redis.call('PEXPIRE', KEYS[1], ttlms)
        end
        return 1
        """;

    public RedisSeatHoldService(IConnectionMultiplexer connectionMultiplexer, IOptions<RedisOptions> options)
    {
        _connectionMultiplexer = connectionMultiplexer;
        _options = options.Value;
    }

    public async Task<IReadOnlyList<Guid>> TryHoldAsync(
        Guid tripId,
        IReadOnlyList<Guid> tripSeatIds,
        Guid userId,
        int fromStopOrder,
        int toStopOrder,
        TimeSpan ttl,
        CancellationToken cancellationToken)
    {
        if (ttl <= TimeSpan.Zero)
        {
            return tripSeatIds;
        }

        var db = _connectionMultiplexer.GetDatabase();
        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var owner = userId.ToString("N");
        var failed = new List<Guid>();

        foreach (var tripSeatId in tripSeatIds)
        {
            var result = (long)await db.ScriptEvaluateAsync(
                    TryHoldScript,
                    [BuildKey(tripId, tripSeatId)],
                    [nowMs, owner, fromStopOrder, toStopOrder, (long)ttl.TotalMilliseconds])
                .WaitAsync(cancellationToken);

            if (result != 1)
            {
                failed.Add(tripSeatId);
            }
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

        foreach (var tripSeatId in tripSeatIds)
        {
            var key = BuildKey(tripId, tripSeatId);
            var entries = await db.HashGetAllAsync(key).WaitAsync(cancellationToken);
            var ownedFields = entries
                .Where(e => TryParseHoldValue(e.Value, out var holder, out _) && holder == userId)
                .Select(e => e.Name)
                .ToArray();
            if (ownedFields.Length > 0)
            {
                await db.HashDeleteAsync(key, ownedFields).WaitAsync(cancellationToken);
            }
        }
    }

    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<SeatHoldInfo>>> GetHeldSeatsAsync(
        Guid tripId,
        CancellationToken cancellationToken)
    {
        var db = _connectionMultiplexer.GetDatabase();
        var pattern = $"{_options.InstanceName}seat-hold:{tripId:N}:*";
        var prefixLength = $"{_options.InstanceName}seat-hold:{tripId:N}:".Length;
        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var result = new Dictionary<Guid, IReadOnlyList<SeatHoldInfo>>();

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

                var entries = await db.HashGetAllAsync(key).WaitAsync(cancellationToken);
                var holds = new List<SeatHoldInfo>();
                foreach (var entry in entries)
                {
                    if (!TryParseSegmentField(entry.Name, out var fromOrder, out var toOrder)
                        || !TryParseHoldValue(entry.Value, out var holderUserId, out var expiresAtMs)
                        || expiresAtMs <= nowMs)
                    {
                        continue;
                    }

                    holds.Add(new SeatHoldInfo(holderUserId, fromOrder, toOrder));
                }

                if (holds.Count > 0)
                {
                    result[tripSeatId] = holds;
                }
            }
        }

        return result;
    }

    public async Task ClearTripAsync(Guid tripId, CancellationToken cancellationToken)
    {
        var db = _connectionMultiplexer.GetDatabase();
        var pattern = $"{_options.InstanceName}seat-hold:{tripId:N}:*";

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
                await db.KeyDeleteAsync(key).WaitAsync(cancellationToken);
            }
        }
    }

    private static bool TryParseSegmentField(RedisValue field, out int fromOrder, out int toOrder)
    {
        fromOrder = 0;
        toOrder = 0;
        var text = field.ToString();
        var sep = text.IndexOf(':', StringComparison.Ordinal);
        return sep > 0
            && int.TryParse(text[..sep], out fromOrder)
            && int.TryParse(text[(sep + 1)..], out toOrder);
    }

    private static bool TryParseHoldValue(RedisValue value, out Guid userId, out long expiresAtMs)
    {
        userId = Guid.Empty;
        expiresAtMs = 0;
        var text = value.ToString();
        var sep = text.IndexOf(':', StringComparison.Ordinal);
        return sep > 0
            && Guid.TryParse(text[..sep], out userId)
            && long.TryParse(text[(sep + 1)..], out expiresAtMs);
    }

    private RedisKey BuildKey(Guid tripId, Guid tripSeatId) =>
        $"{_options.InstanceName}seat-hold:{tripId:N}:{tripSeatId:N}";
}
