using System.Collections.Concurrent;
using SaigonWaterbus.Application.Common.Interfaces;

namespace SaigonWaterbus.Infrastructure.Auth;

public sealed class InMemoryGoogleLoginTempStore : IGoogleLoginTempStore
{
    private readonly ConcurrentDictionary<string, GoogleLoginTempSession> _sessions = new();
    private readonly TimeProvider _timeProvider;

    public InMemoryGoogleLoginTempStore(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
    }

    public Task SaveAsync(GoogleLoginTempSession session, CancellationToken cancellationToken)
    {
        _sessions[session.TempToken] = session;
        return Task.CompletedTask;
    }

    public Task<GoogleLoginTempSession?> GetAsync(string tempToken, CancellationToken cancellationToken)
    {
        if (!_sessions.TryGetValue(tempToken, out var session))
        {
            return Task.FromResult<GoogleLoginTempSession?>(null);
        }

        if (session.ExpiresAt <= _timeProvider.GetUtcNow())
        {
            _sessions.TryRemove(tempToken, out _);
            return Task.FromResult<GoogleLoginTempSession?>(null);
        }

        return Task.FromResult<GoogleLoginTempSession?>(session);
    }

    public Task RemoveAsync(string tempToken, CancellationToken cancellationToken)
    {
        _sessions.TryRemove(tempToken, out _);
        return Task.CompletedTask;
    }
}
