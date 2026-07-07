using System.Collections.Concurrent;
using SaigonWaterbus.Application.Common.Interfaces;

namespace SaigonWaterbus.Infrastructure.Redis;

/// <summary>
/// Fallback giữ ghế trong bộ nhớ khi không bật Redis (môi trường dev / single instance).
/// Đăng ký dưới dạng singleton.
/// </summary>
public sealed class InMemorySeatHoldService : ISeatHoldService
{
    private sealed record SeatHold(Guid UserId, DateTimeOffset ExpiresAt);

    private readonly ConcurrentDictionary<(Guid TripId, Guid TripSeatId), SeatHold> _holds = new();
    private readonly TimeProvider _timeProvider;

    public InMemorySeatHoldService(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public Task<IReadOnlyList<Guid>> TryHoldAsync(
        Guid tripId,
        IReadOnlyList<Guid> tripSeatIds,
        Guid userId,
        TimeSpan ttl,
        CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();
        if (ttl <= TimeSpan.Zero)
        {
            return Task.FromResult<IReadOnlyList<Guid>>(tripSeatIds);
        }

        PruneExpired(now);
        var newHold = new SeatHold(userId, now.Add(ttl));
        var failed = new List<Guid>();

        foreach (var tripSeatId in tripSeatIds)
        {
            var key = (tripId, tripSeatId);
            var current = _holds.AddOrUpdate(
                key,
                newHold,
                (_, existing) => existing.UserId == userId || existing.ExpiresAt <= now ? newHold : existing);
            if (current.UserId != userId)
            {
                failed.Add(tripSeatId);
            }
        }

        return Task.FromResult<IReadOnlyList<Guid>>(failed);
    }

    public Task ReleaseAsync(
        Guid tripId,
        IReadOnlyList<Guid> tripSeatIds,
        Guid userId,
        CancellationToken cancellationToken)
    {
        foreach (var tripSeatId in tripSeatIds)
        {
            var key = (tripId, tripSeatId);
            if (_holds.TryGetValue(key, out var hold) && hold.UserId == userId)
            {
                _holds.TryRemove(new KeyValuePair<(Guid, Guid), SeatHold>(key, hold));
            }
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyDictionary<Guid, Guid>> GetHeldSeatsAsync(
        Guid tripId,
        CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();
        PruneExpired(now);

        var result = _holds
            .Where(x => x.Key.TripId == tripId && x.Value.ExpiresAt > now)
            .ToDictionary(x => x.Key.TripSeatId, x => x.Value.UserId);

        return Task.FromResult<IReadOnlyDictionary<Guid, Guid>>(result);
    }

    private void PruneExpired(DateTimeOffset now)
    {
        foreach (var entry in _holds)
        {
            if (entry.Value.ExpiresAt <= now)
            {
                _holds.TryRemove(entry);
            }
        }
    }
}
