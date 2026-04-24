namespace SaigonWaterbus.Application.Common.Interfaces;

public interface IUserContext
{
    int? UserId { get; }

    bool IsAuthenticated { get; }

    string? IpAddress { get; }

    string? UserAgent { get; }
}
