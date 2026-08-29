using SaigonWaterbus.Application.Common.Interfaces;

namespace SaigonWaterbus.Infrastructure.Redis;

/// <summary>
/// Fallback giữ ghế trong bộ nhớ khi không bật Redis (môi trường dev / single instance).
/// Đăng ký dưới dạng singleton. Mỗi ghế có thể có nhiều lượt giữ trên các chặng không giao nhau.
/// </summary>
public sealed class InMemorySeatHoldService : ISeatHoldService
{
    private sealed record SeatHold(Guid UserId, int FromStopOrder, int ToStopOrder, DateTimeOffset ExpiresAt);

    private readonly object _lock = new();
    private readonly Dictionary<(Guid TripId, Guid TripSeatId), List<SeatHold>> _holds = new();
    private readonly TimeProvider _timeProvider;

    public InMemorySeatHoldService(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public Task<IReadOnlyList<Guid>> TryHoldAsync(
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
            return Task.FromResult<IReadOnlyList<Guid>>(tripSeatIds);
        }

        var now = _timeProvider.GetUtcNow();
        var failed = new List<Guid>();

        lock (_lock)
        {
            PruneExpired(now);

            foreach (var tripSeatId in tripSeatIds)
            {
                var key = (tripId, tripSeatId);
                _holds.TryGetValue(key, out var holds);

                var conflict = holds is not null && holds.Any(h =>
                    h.UserId != userId
                    && h.FromStopOrder < toStopOrder
                    && fromStopOrder < h.ToStopOrder);
                if (conflict)
                {
                    failed.Add(tripSeatId);
                    continue;
                }

                holds ??= _holds[key] = [];
                holds.RemoveAll(h =>
                    h.UserId == userId
                    && h.FromStopOrder == fromStopOrder
                    && h.ToStopOrder == toStopOrder);
                holds.Add(new SeatHold(userId, fromStopOrder, toStopOrder, now.Add(ttl)));
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
        lock (_lock)
        {
            foreach (var tripSeatId in tripSeatIds)
            {
                if (_holds.TryGetValue((tripId, tripSeatId), out var holds))
                {
                    holds.RemoveAll(h => h.UserId == userId);
                    if (holds.Count == 0)
                    {
                        _holds.Remove((tripId, tripSeatId));
                    }
                }
            }
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyDictionary<Guid, IReadOnlyList<SeatHoldInfo>>> GetHeldSeatsAsync(
        Guid tripId,
        CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();
        var result = new Dictionary<Guid, IReadOnlyList<SeatHoldInfo>>();

        lock (_lock)
        {
            PruneExpired(now);

            foreach (var entry in _holds)
            {
                if (entry.Key.TripId != tripId || entry.Value.Count == 0)
                {
                    continue;
                }

                result[entry.Key.TripSeatId] = entry.Value
                    .Select(h => new SeatHoldInfo(h.UserId, h.FromStopOrder, h.ToStopOrder))
                    .ToList();
            }
        }

        return Task.FromResult<IReadOnlyDictionary<Guid, IReadOnlyList<SeatHoldInfo>>>(result);
    }

    public Task ClearTripAsync(Guid tripId, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            foreach (var key in _holds.Keys.Where(x => x.TripId == tripId).ToList())
            {
                _holds.Remove(key);
            }
        }

        return Task.CompletedTask;
    }

    private void PruneExpired(DateTimeOffset now)
    {
        foreach (var entry in _holds.ToList())
        {
            entry.Value.RemoveAll(h => h.ExpiresAt <= now);
            if (entry.Value.Count == 0)
            {
                _holds.Remove(entry.Key);
            }
        }
    }
}
