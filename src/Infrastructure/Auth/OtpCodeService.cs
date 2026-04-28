using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using SaigonWaterbus.Application.Common.Interfaces;

namespace SaigonWaterbus.Infrastructure.Auth;

public sealed class OtpCodeService : IOtpCodeService
{
    private readonly OtpOptions _otpOptions;

    public OtpCodeService(IOptions<OtpOptions> otpOptions)
    {
        _otpOptions = otpOptions.Value;
    }

    public string GenerateCode()
    {
        var maxValue = (int)Math.Pow(10, _otpOptions.CodeLength);
        var value = RandomNumberGenerator.GetInt32(0, maxValue);
        return value.ToString($"D{_otpOptions.CodeLength}");
    }

    public string MaskEmail(string email)
    {
        var parts = email.Split('@', 2, StringSplitOptions.TrimEntries);
        if (parts.Length != 2)
        {
            return "***";
        }

        var local = parts[0];
        var maskedLocal = local.Length switch
        {
            <= 2 => $"{local[0]}*",
            _ => $"{local[0]}***{local[^1]}"
        };

        return $"{maskedLocal}@{parts[1]}";
    }

    public string MaskPhone(string phoneNumber)
    {
        var digits = new string(phoneNumber.Where(char.IsDigit).ToArray());
        if (digits.Length < 4)
        {
            return "***";
        }

        if (digits.Length <= 6)
        {
            return $"{digits[..1]}***{digits[^1..]}";
        }

        return $"{digits[..3]}****{digits[^3..]}";
    }
}
