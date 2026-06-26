using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Application.Common.Interfaces;

public interface IOtpCache
{
    Task StoreAsync(OtpChallenge challenge, string codeHash, CancellationToken cancellationToken);

    Task<string?> GetCodeHashAsync(Guid challengeId, CancellationToken cancellationToken);

    Task RemoveAsync(Guid challengeId, CancellationToken cancellationToken);
}
