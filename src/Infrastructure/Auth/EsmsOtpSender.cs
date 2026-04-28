using Microsoft.Extensions.Options;
using SaigonWaterbus.Application.Common.Exceptions;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Infrastructure.Auth;

public sealed class EsmsOtpSender : ISmsOtpSender
{
    private readonly EsmsSmsSender _smsSender;
    private readonly IOptionsMonitor<EsmsOptions> _optionsMonitor;

    public EsmsOtpSender(
        EsmsSmsSender smsSender,
        IOptionsMonitor<EsmsOptions> optionsMonitor)
    {
        _smsSender = smsSender;
        _optionsMonitor = optionsMonitor;
    }

    public async Task SendAsync(string phoneNumber, string code, OtpPurpose purpose, string? recipientName, CancellationToken cancellationToken)
    {
        var options = _optionsMonitor.CurrentValue;
        var brandname = string.IsNullOrWhiteSpace(options.Brandname)
            ? "Baotrixemay"
            : options.Brandname.Trim();
        var content = BuildContent(code, purpose, brandname);

        EsmsSendResult result;
        try
        {
            result = await _smsSender.SendAsync(
                new EsmsSendRequest(
                    Phone: phoneNumber,
                    Content: content),
                cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            throw new OtpDispatchException($"eSMS configuration is invalid: {ex.Message}");
        }
        catch (HttpRequestException ex)
        {
            throw new OtpDispatchException($"Unable to connect to eSMS: {ex.Message}");
        }

        if (!result.IsAccepted)
        {
            throw new OtpDispatchException(
                $"eSMS rejected OTP SMS request. CodeResult={result.CodeResult ?? "(n/a)"}, ErrorMessage={result.ErrorMessage ?? "(n/a)"}");
        }
    }

    private static string BuildContent(string code, OtpPurpose purpose, string brandname)
    {
        return purpose switch
        {
            OtpPurpose.Register => $"{code} la ma xac minh dang ky {brandname} cua ban",
            OtpPurpose.ForgotPassword => $"{code} la ma xac minh dat lai mat khau {brandname} cua ban",
            _ => $"{code} la ma xac minh tai khoan {brandname} cua ban"
        };
    }
}
