using Microsoft.Extensions.Options;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Infrastructure.Options;
using StackExchange.Redis;

namespace SaigonWaterbus.Infrastructure.Redis;

public sealed class RedisOtpCache : IOtpCache
{
    private readonly IConnectionMultiplexer _connectionMultiplexer;
    private readonly RedisOptions _options;

    public RedisOtpCache(IConnectionMultiplexer connectionMultiplexer, IOptions<RedisOptions> options)
    {
        _connectionMultiplexer = connectionMultiplexer;
        _options = options.Value;
    }

    public async Task StoreAsync(OtpChallenge challenge, string codeHash, CancellationToken cancellationToken)
    {
        var ttl = challenge.ExpiresAt - DateTimeOffset.UtcNow;
        if (ttl <= TimeSpan.Zero)
        {
            return;
        }

        var db = _connectionMultiplexer.GetDatabase();
        var key = BuildKey(challenge.Id);
        var values = new HashEntry[]
        {
            new("code_hash", codeHash),
            new("max_attempts", challenge.MaxAttempts),
            new("expires_at", challenge.ExpiresAt.ToUnixTimeMilliseconds())
        };

        await db.HashSetAsync(key, values).WaitAsync(cancellationToken);
        await db.KeyExpireAsync(key, ttl).WaitAsync(cancellationToken);
    }

    public async Task<string?> GetCodeHashAsync(Guid challengeId, CancellationToken cancellationToken)
    {
        var value = await _connectionMultiplexer.GetDatabase()
            .HashGetAsync(BuildKey(challengeId), "code_hash")
            .WaitAsync(cancellationToken);

        return value.HasValue ? value.ToString() : null;
    }

    public async Task RemoveAsync(Guid challengeId, CancellationToken cancellationToken)
    {
        await _connectionMultiplexer.GetDatabase()
            .KeyDeleteAsync(BuildKey(challengeId))
            .WaitAsync(cancellationToken);
    }

    private RedisKey BuildKey(Guid challengeId) =>
        $"{_options.InstanceName}otp:{challengeId:N}";
}
