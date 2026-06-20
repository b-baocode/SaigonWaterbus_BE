namespace SaigonWaterbus.Application.Common.Exceptions;

public sealed class ProfileImageStorageException : Exception
{
    public ProfileImageStorageException(string message)
        : base(message)
    {
    }

    public ProfileImageStorageException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
