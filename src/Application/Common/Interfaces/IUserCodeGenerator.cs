namespace SaigonWaterbus.Application.Common.Interfaces;

public interface IUserCodeGenerator
{
    Task<string> GenerateNextCodeAsync(string roleCode, CancellationToken cancellationToken);
}
