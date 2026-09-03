namespace SaigonWaterbus.Application.Common.Exceptions;

public sealed class EmailDispatchException : Exception
{
    public EmailDispatchException(string message)
        : base(message)
    {
    }

    public EmailDispatchException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
