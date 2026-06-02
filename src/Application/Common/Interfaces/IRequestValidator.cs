namespace SaigonWaterbus.Application.Common.Interfaces;

public interface IRequestValidator
{
    Task ValidateAsync<TRequest>(TRequest request, CancellationToken cancellationToken);
}
