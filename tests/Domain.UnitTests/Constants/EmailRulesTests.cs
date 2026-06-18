using NUnit.Framework;
using SaigonWaterbus.Domain.Constants;
using Shouldly;

namespace SaigonWaterbus.Domain.UnitTests.Constants;

public class EmailRulesTests
{
    [TestCase("user@gmail.com", true)]
    [TestCase("student@fpt.edu.vn", true)]
    [TestCase("USER@GMAIL.COM", true)]
    [TestCase("user@yahoo.com", false)]
    [TestCase("user@fpt.com", false)]
    [TestCase("invalid-email", false)]
    [TestCase("", false)]
    public void HasAllowedRegistrationDomain_ShouldValidateExpectedDomains(string email, bool expected)
    {
        var result = EmailRules.HasAllowedRegistrationDomain(email);

        result.ShouldBe(expected);
    }
}
