namespace SaigonWaterbus.Application.Common.Interfaces;

public interface ISecretHasher
{
    string Hash(string secret);

    bool Verify(string secret, string hash);
}
