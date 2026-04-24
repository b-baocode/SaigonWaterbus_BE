using MediatR.Pipeline;
using Microsoft.Extensions.Logging;
using System.Reflection;

namespace SaigonWaterbus.Application.Common.Behaviours;

public class LoggingBehaviour<TRequest> : IRequestPreProcessor<TRequest>
    where TRequest : notnull
{
    private static readonly HashSet<string> SensitivePropertyNames =
    [
        "Password",
        "CurrentPassword",
        "NewPassword",
        "Code",
        "RefreshToken"
    ];

    private readonly ILogger _logger;

    public LoggingBehaviour(ILogger<TRequest> logger)
    {
        _logger = logger;
    }

    public Task Process(TRequest request, CancellationToken cancellationToken)
    {
        var requestName = typeof(TRequest).Name;

        _logger.LogInformation("SaigonWaterbus Request: {Name} {@Request}",
            requestName, SanitizeRequest(request));

        return Task.CompletedTask;
    }

    private static object SanitizeRequest(TRequest request)
    {
        var properties = typeof(TRequest).GetProperties(BindingFlags.Instance | BindingFlags.Public);
        if (properties.Length == 0)
        {
            return request;
        }

        var payload = new Dictionary<string, object?>(properties.Length, StringComparer.Ordinal);
        foreach (var property in properties)
        {
            var value = property.GetValue(request);
            payload[property.Name] = SensitivePropertyNames.Contains(property.Name)
                ? "***"
                : value;
        }

        return payload;
    }
}
