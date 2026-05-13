using SaigonWaterbus.Domain.Constants;
using NUnit.Framework;
using Shouldly;

namespace SaigonWaterbus.Domain.UnitTests.Constants;

public class PhoneRulesTests
{
    [TestCase("0901234567", true)]
    [TestCase("+84901234567", true)]
    [TestCase("84901234567", true)]
    [TestCase("+84 901 234 567", true)]
    [TestCase("090123456", false)]
    [TestCase("0101234567", false)]
    [TestCase("+12025550123", true)]
    [TestCase("+81312345678", true)]
    [TestCase("+33123456789", true)]
    [TestCase("abc0901234567", false)]
    public void IsValidShouldValidateInternationalPhoneNumbers(string phoneNumber, bool expected)
    {
        var result = PhoneRules.IsValid(phoneNumber);

        result.ShouldBe(expected);
    }

    [TestCase("0901234567")]
    [TestCase("+84901234567")]
    [TestCase("84901234567")]
    public void TryNormalizeShouldUseE164FormatForLookup(string phoneNumber)
    {
        var result = PhoneRules.TryNormalize(phoneNumber, out var normalizedPhoneNumber);

        result.ShouldBeTrue();
        normalizedPhoneNumber.ShouldBe("+84901234567");
    }

    [Test]
    public void ToInternationalFormatShouldUseE164FormatForDisplay()
    {
        var result = PhoneRules.ToInternationalFormat("0901234567");

        result.ShouldBe("+84901234567");
    }

    [TestCase("0901234567", true)]
    [TestCase("+84901234567", true)]
    [TestCase("+12025550123", false)]
    [TestCase("+81312345678", false)]
    public void IsVietnamPhoneShouldDetectVietnamCountryCode(string phoneNumber, bool expected)
    {
        var result = PhoneRules.IsVietnamPhone(phoneNumber);

        result.ShouldBe(expected);
    }
}
