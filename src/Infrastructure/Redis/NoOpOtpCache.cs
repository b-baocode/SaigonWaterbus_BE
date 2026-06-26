using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Infrastructure.Redis;

public sealed class NoOpOtpCache : IOtpCache
{
    public Task StoreAsync(OtpChallenge challenge, string codeHash, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task<string?> GetCodeHashAsync(Guid challengeId, CancellationToken cancellationToken) =>
        Task.FromResult<string?>(null);

    public Task RemoveAsync(Guid challengeId, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
