using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using SaigonWaterbus.Infrastructure.Options;
using StackExchange.Redis;

namespace SaigonWaterbus.Web.Infrastructure;

public interface IEndpointResponseCache
{
    Task<T?> GetOrCreateAsync<T>(
        string key,
        TimeSpan ttl,
        Func<CancellationToken, Task<T>> factory,
        CancellationToken cancellationToken);
}

public sealed class RedisBackedEndpointResponseCache : IEndpointResponseCache
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IMemoryCache _memoryCache;
    private readonly IServiceProvider _serviceProvider;
    private readonly RedisOptions _redisOptions;
    private readonly ILogger<RedisBackedEndpointResponseCache> _logger;

    public RedisBackedEndpointResponseCache(
        IMemoryCache memoryCache,
        IServiceProvider serviceProvider,
        IOptions<RedisOptions> redisOptions,
        ILogger<RedisBackedEndpointResponseCache> logger)
    {
        _memoryCache = memoryCache;
        _serviceProvider = serviceProvider;
        _redisOptions = redisOptions.Value;
        _logger = logger;
    }

    public async Task<T?> GetOrCreateAsync<T>(
        string key,
        TimeSpan ttl,
        Func<CancellationToken, Task<T>> factory,
        CancellationToken cancellationToken)
    {
        if (_memoryCache.TryGetValue<T>(key, out var memoryValue))
        {
            return memoryValue;
        }

        var redisKey = BuildRedisKey(key);
        var database = ResolveRedisDatabase();
        if (database is not null)
        {
            var cachedJson = await TryGetRedisStringAsync(database, redisKey, cancellationToken);
            if (cachedJson is not null)
            {
                var redisValue = JsonSerializer.Deserialize<T>(cachedJson, JsonOptions);
                if (redisValue is not null)
                {
                    _memoryCache.Set(key, redisValue, ttl);
                    return redisValue;
                }
            }
        }

        var value = await factory(cancellationToken);
        if (value is null)
        {
            return value;
        }

        _memoryCache.Set(key, value, ttl);
        if (database is not null)
        {
            await TrySetRedisStringAsync(database, redisKey, value, ttl, cancellationToken);
        }

        return value;
    }

    private IDatabase? ResolveRedisDatabase()
    {
        if (!_redisOptions.Enabled)
        {
            return null;
        }

        var multiplexer = _serviceProvider.GetService<IConnectionMultiplexer>();
        return multiplexer?.GetDatabase();
    }

    private async Task<string?> TryGetRedisStringAsync(
        IDatabase database,
        RedisKey key,
        CancellationToken cancellationToken)
    {
        try
        {
            var value = await database.StringGetAsync(key).WaitAsync(cancellationToken);
            return value.HasValue ? value.ToString() : null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Redis response cache read failed for key {CacheKey}.", key);
            return null;
        }
    }

    private async Task TrySetRedisStringAsync<T>(
        IDatabase database,
        RedisKey key,
        T value,
        TimeSpan ttl,
        CancellationToken cancellationToken)
    {
        try
        {
            var json = JsonSerializer.Serialize(value, JsonOptions);
            await database.StringSetAsync(key, json, ttl).WaitAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Redis response cache write failed for key {CacheKey}.", key);
        }
    }

    private RedisKey BuildRedisKey(string key) =>
        $"{_redisOptions.InstanceName}response-cache:{key}";
}
