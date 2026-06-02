using Microsoft.Extensions.DependencyInjection;
using SaigonWaterbus.Application.Common.Interfaces;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.Common.Validation;

public sealed class RequestValidator : IRequestValidator
{
    private readonly IServiceProvider _serviceProvider;

    public RequestValidator(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public async Task ValidateAsync<TRequest>(TRequest request, CancellationToken cancellationToken)
    {
        var validators = _serviceProvider.GetServices<IValidator<TRequest>>().ToArray();
        if (validators.Length == 0)
        {
            return;
        }

        var validationResults = await Task.WhenAll(validators.Select(x =>
            x.ValidateAsync(new ValidationContext<TRequest>(request), cancellationToken)));

        var failures = validationResults
            .SelectMany(x => x.Errors)
            .Where(x => x is not null)
            .ToList();

        if (failures.Count != 0)
        {
            throw new ValidationException(failures);
        }
    }
}
