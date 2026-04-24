using Microsoft.Extensions.Options;
using SaigonWaterbus.Application.Common.Interfaces;

namespace SaigonWaterbus.Infrastructure.Auth;

public sealed class OtpPolicyAccessor : IOtpPolicy
{
    private readonly IOptionsMonitor<OtpOptions> _optionsMonitor;

    public OtpPolicyAccessor(IOptionsMonitor<OtpOptions> optionsMonitor)
    {
        _optionsMonitor = optionsMonitor;
    }

    public int ExpirationMinutes => _optionsMonitor.CurrentValue.ExpirationMinutes;

    public int ResendSeconds => _optionsMonitor.CurrentValue.ResendSeconds;

    public int MaxAttempts => _optionsMonitor.CurrentValue.MaxAttempts;
}
