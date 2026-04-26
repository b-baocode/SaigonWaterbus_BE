namespace SaigonWaterbus.Application.Common.Exceptions;

public sealed class ExternalAuthenticationException : Exception
{
    public ExternalAuthenticationException(string message)
        : base(message)
    {
    }
}
