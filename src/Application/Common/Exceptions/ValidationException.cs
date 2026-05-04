using FluentValidation.Results;

namespace SaigonWaterbus.Application.Common.Exceptions;

public class ValidationException : Exception
{
    public ValidationException()
        : base("One or more validation failures have occurred.")
    {
        Errors = new Dictionary<string, string[]>();
    }

    public ValidationException(IEnumerable<ValidationFailure> failures)
        : this()
    {
        Errors = failures
            .GroupBy(e => ToCamelCase(e.PropertyName), e => e.ErrorMessage)
            .ToDictionary(failureGroup => failureGroup.Key, failureGroup => failureGroup.ToArray());
    }

    public IDictionary<string, string[]> Errors { get; }

    private static string ToCamelCase(string propertyName)
    {
        if (string.IsNullOrWhiteSpace(propertyName) || char.IsLower(propertyName[0]))
        {
            return propertyName;
        }

        return string.Create(
            propertyName.Length,
            propertyName,
            static (destination, source) =>
            {
                source.AsSpan().CopyTo(destination);
                destination[0] = char.ToLowerInvariant(destination[0]);
            });
    }
}
