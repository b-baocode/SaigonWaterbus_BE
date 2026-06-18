using NUnit.Framework;
using SaigonWaterbus.Domain.Constants;
using Shouldly;

namespace SaigonWaterbus.Domain.UnitTests.Constants;

public class PasswordRulesTests
{
    [TestCase("Password@", true)]
    [TestCase("P@ssword123", true)]
    [TestCase("password@", false)]
    [TestCase("PASSWORD@", false)]
    [TestCase("Password1", false)]
    [TestCase("Pass@", false)]
    [TestCase("", false)]
    public void IsStrongShouldRequireConfiguredPasswordRules(string password, bool expected)
    {
        var result = PasswordRules.IsStrong(password);

        result.ShouldBe(expected);
    }
}
