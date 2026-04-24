namespace SaigonWaterbus.Application.Common.Exceptions;

public sealed class OtpDispatchException : Exception
{
    public OtpDispatchException(string message)
        : base(message)
    {
    }
}
