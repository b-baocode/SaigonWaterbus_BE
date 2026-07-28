using Microsoft.Extensions.Options;
using SaigonWaterbus.Application.Common.Exceptions;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Infrastructure.Auth;

public sealed class EsmsOtpSender : ISmsOtpSender
{
    private static readonly HashSet<string> VinaMobilePrefixes =
    [
        "81", "82", "83", "84", "85", "88", "91", "94"
    ];

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
        var content = BuildContent(phoneNumber, code, purpose, brandname, options);

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

    private static string BuildContent(
        string phoneNumber,
        string code,
        OtpPurpose purpose,
        string brandname,
        EsmsOptions options)
    {
        var isVinaPhone = IsVinaPhoneNumber(phoneNumber);
        var template = purpose switch
        {
            OtpPurpose.Register or OtpPurpose.PhoneChange => isVinaPhone
                ? ResolveTemplate(options.VinaRegisterContentTemplate, options.RegisterContentTemplate, options.DefaultContent)
                : ResolveTemplate(options.RegisterContentTemplate, options.DefaultContent),
            OtpPurpose.ForgotPassword => isVinaPhone
                ? ResolveTemplate(options.VinaForgotPasswordContentTemplate, options.ForgotPasswordContentTemplate, options.DefaultContent)
                : ResolveTemplate(options.ForgotPasswordContentTemplate, options.DefaultContent),
            OtpPurpose.Refund => isVinaPhone
                ? ResolveTemplate(
                    options.VinaRefundContentTemplate,
                    options.VinaRegisterContentTemplate,
                    options.RefundContentTemplate,
                    options.RegisterContentTemplate,
                    options.DefaultContent)
                : ResolveTemplate(
                    options.RefundContentTemplate,
                    options.RegisterContentTemplate,
                    options.DefaultContent),
            _ => isVinaPhone
                ? ResolveTemplate(options.VinaDefaultContentTemplate, options.DefaultContentTemplate, options.DefaultContent)
                : ResolveTemplate(options.DefaultContentTemplate, options.DefaultContent)
        };

        return template
            .Replace("{code}", code, StringComparison.OrdinalIgnoreCase)
            .Replace("{brandname}", brandname, StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveTemplate(params string?[] templates)
    {
        foreach (var template in templates)
        {
            if (!string.IsNullOrWhiteSpace(template))
            {
                return template.Trim()
                    .Replace("123456", "{code}", StringComparison.Ordinal);
            }
        }

        return "{code} la ma xac minh tai khoan {brandname} cua ban";
    }

    private static bool IsVinaPhoneNumber(string phoneNumber)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber))
        {
            return false;
        }

        var digits = new string(phoneNumber.Where(char.IsDigit).ToArray());
        if (digits.StartsWith("84", StringComparison.Ordinal))
        {
            digits = digits[2..];
        }
        else if (digits.StartsWith("0", StringComparison.Ordinal))
        {
            digits = digits[1..];
        }

        if (digits.Length < 2)
        {
            return false;
        }

        return VinaMobilePrefixes.Contains(digits[..2]);
    }
}
