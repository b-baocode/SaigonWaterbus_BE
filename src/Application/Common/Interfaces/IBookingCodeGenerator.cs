namespace SaigonWaterbus.Application.Common.Interfaces;

public interface IBookingCodeGenerator
{
    Task<string> GenerateAsync(CancellationToken cancellationToken);
}
