using System.Security.Cryptography;
using System.Text;
using NUnit.Framework;
using SaigonWaterbus.Infrastructure.Payments;
using Shouldly;

namespace SaigonWaterbus.Application.UnitTests.Payments;

public class PayOsSignatureTests
{
    [Test]
    public void CreatePayoutRequestSignatureUsesEncodedSortedCompactJsonPayload()
    {
        const string checksumKey = "payout-checksum-key";
        var category = new[] { "refund", "custom-booking" };
        var expectedData =
            "amount=1000000&category=%5B%22refund%22%2C%22custom-booking%22%5D&description=Hoan%20tien%20SWB%201234ABCD&referenceId=CBR-123&toAccountNumber=22929167&toBin=970416";

        var data = PayOsSignature.CreatePayoutRequestSignatureData(
            "CBR-123",
            1000000,
            "Hoan tien SWB 1234ABCD",
            "970416",
            "22929167",
            category);
        var signature = PayOsSignature.CreatePayoutRequestSignature(
            "CBR-123",
            1000000,
            "Hoan tien SWB 1234ABCD",
            "970416",
            "22929167",
            category,
            checksumKey);

        data.ShouldBe(expectedData);
        signature.ShouldBe(HmacSha256(expectedData, checksumKey));
        signature.ShouldBe(signature.ToLowerInvariant());
    }

    private static string HmacSha256(string data, string key)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(key));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
