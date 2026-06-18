using NUnit.Framework;
using SaigonWaterbus.Domain.Constants;
using Shouldly;

namespace SaigonWaterbus.Domain.UnitTests.Constants;

public class RoleRulesTests
{
    [Test]
    public void GetAssignableRolesShouldAllowManagerToAssignCustomerAndStaff()
    {
        var result = RoleRules.GetAssignableRoles(Roles.Manager);

        result.ShouldBe([Roles.Customer, Roles.Staff], ignoreOrder: true);
    }

    [TestCase(Roles.Customer, true)]
    [TestCase(Roles.Staff, true)]
    [TestCase(Roles.Manager, false)]
    [TestCase(Roles.Administrator, false)]
    public void CanChangeRoleShouldMatchManagerAssignableRoles(string targetRoleCode, bool expected)
    {
        var result = RoleRules.CanChangeRole(Roles.Manager, targetRoleCode);

        result.ShouldBe(expected);
    }
}
