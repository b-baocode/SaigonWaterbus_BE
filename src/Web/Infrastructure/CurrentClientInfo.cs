using SaigonWaterbus.Application.Common.Interfaces;

namespace SaigonWaterbus.Web.Infrastructure;

public sealed class CurrentClientInfo : IClientInfoProvider
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentClientInfo(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public string? GetDeviceInfo()
    {
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext is null)
        {
            return null;
        }

        var userAgent = httpContext.Request.Headers.UserAgent.ToString();
        var ipAddress = httpContext.Connection.RemoteIpAddress?.ToString();

        if (string.IsNullOrWhiteSpace(userAgent))
        {
            return ipAddress;
        }

        return string.IsNullOrWhiteSpace(ipAddress)
            ? userAgent
            : $"{userAgent} ({ipAddress})";
    }
}
