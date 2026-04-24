using System.Security.Cryptography;
using SaigonWaterbus.Application.Common.Interfaces;

namespace SaigonWaterbus.Infrastructure.Auth;

public sealed class Pbkdf2SecretHasher : ISecretHasher
{
    private const int SaltSize = 16;
    private const int HashSize = 32;
    private const int Iterations = 100_000;

    public string Hash(string secret)
    {
        Guard.Against.NullOrWhiteSpace(secret);

        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            secret,
            salt,
            Iterations,
            HashAlgorithmName.SHA256,
            HashSize);

        return $"PBKDF2.SHA256.{Iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
    }

    public bool Verify(string secret, string hash)
    {
        Guard.Against.NullOrWhiteSpace(secret);
        Guard.Against.NullOrWhiteSpace(hash);

        var segments = hash.Split('.', StringSplitOptions.TrimEntries);
        if (segments.Length != 5 || !int.TryParse(segments[2], out var iterations))
        {
            return false;
        }

        var salt = Convert.FromBase64String(segments[3]);
        var expectedHash = Convert.FromBase64String(segments[4]);
        var actualHash = Rfc2898DeriveBytes.Pbkdf2(
            secret,
            salt,
            iterations,
            HashAlgorithmName.SHA256,
            expectedHash.Length);

        return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
    }
}
