using Microsoft.Extensions.Options;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Infrastructure.Options;
using StackExchange.Redis;

namespace SaigonWaterbus.Infrastructure.Redis;

public sealed class RedisPaymentProcessingLock : IPaymentProcessingLock
{
    private readonly IConnectionMultiplexer _connectionMultiplexer;
    private readonly RedisOptions _options;

    public RedisPaymentProcessingLock(IConnectionMultiplexer connectionMultiplexer, IOptions<RedisOptions> options)
    {
        _connectionMultiplexer = connectionMultiplexer;
        _options = options.Value;
    }

    public async Task<IPaymentProcessingLockHandle> TryAcquireAsync(
        string paymentCode,
        CancellationToken cancellationToken)
    {
        var db = _connectionMultiplexer.GetDatabase();
        var key = BuildKey(paymentCode);
        var token = Guid.NewGuid().ToString("N");
        var ttl = TimeSpan.FromSeconds(Math.Max(_options.LockTtlSeconds, 5));
        var acquired = await db.StringSetAsync(key, token, ttl, When.NotExists).WaitAsync(cancellationToken);

        return new RedisPaymentProcessingLockHandle(db, key, token, acquired);
    }

    private RedisKey BuildKey(string paymentCode) =>
        $"{_options.InstanceName}payment-lock:{paymentCode}";

    private sealed class RedisPaymentProcessingLockHandle : IPaymentProcessingLockHandle
    {
        private readonly IDatabase _database;
        private readonly RedisKey _key;
        private readonly RedisValue _token;

        public RedisPaymentProcessingLockHandle(IDatabase database, RedisKey key, RedisValue token, bool acquired)
        {
            _database = database;
            _key = key;
            _token = token;
            Acquired = acquired;
        }

        public bool Acquired { get; }

        public async ValueTask DisposeAsync()
        {
            if (!Acquired)
            {
                return;
            }

            var existing = await _database.StringGetAsync(_key);
            if (existing == _token)
            {
                await _database.KeyDeleteAsync(_key);
            }
        }
    }
}
