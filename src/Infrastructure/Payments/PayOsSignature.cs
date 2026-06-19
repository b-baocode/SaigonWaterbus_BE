using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace SaigonWaterbus.Infrastructure.Payments;

internal static class PayOsSignature
{
    public static string CreatePayoutRequestSignature(
        string referenceId,
        long amount,
        string description,
        string toBin,
        string toAccountNumber,
        string checksumKey)
    {
        var data =
            $"amount={amount}&description={description}&referenceId={referenceId}&toAccountNumber={toAccountNumber}&toBin={toBin}";
        return HmacSha256(data, checksumKey);
    }

    public static string CreatePaymentRequestSignature(
        long amount,
        string cancelUrl,
        string description,
        long orderCode,
        string returnUrl,
        string checksumKey)
    {
        var data =
            $"amount={amount}&cancelUrl={cancelUrl}&description={description}&orderCode={orderCode}&returnUrl={returnUrl}";
        return HmacSha256(data, checksumKey);
    }

    public static string CreateSignatureFromData(
        IReadOnlyDictionary<string, object?> data,
        string checksumKey)
    {
        var queryString = string.Join(
            '&',
            data
                .Where(x => x.Value is not null)
                .OrderBy(x => x.Key, StringComparer.Ordinal)
                .Select(x => $"{x.Key}={FormatValue(x.Value)}"));

        return HmacSha256(queryString, checksumKey);
    }

    private static string HmacSha256(string data, string key)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(key));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string FormatValue(object? value)
    {
        if (value is null)
        {
            return string.Empty;
        }

        return Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
    }
}
