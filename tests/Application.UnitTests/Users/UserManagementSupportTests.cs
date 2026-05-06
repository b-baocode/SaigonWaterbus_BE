using SaigonWaterbus.Application.Common.Exceptions;
using SaigonWaterbus.Application.Users;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;
using NUnit.Framework;
using Shouldly;

namespace SaigonWaterbus.Application.UnitTests.Users;

public class UserManagementSupportTests
{
    [Test]
    public void ManagerCanCreateStaff()
    {
        var actor = UserWithRole(1, Roles.ManagerSystemName);
        var targetRole = Role(Roles.StaffSystemName);

        Should.NotThrow(() => UserManagementSupport.EnsureCanCreateRole(actor, targetRole, "roleId"));
    }

    [Test]
    public void ManagerCannotCreateCustomer()
    {
        var actor = UserWithRole(1, Roles.ManagerSystemName);
        var targetRole = Role(Roles.CustomerSystemName);

        Should.Throw<ValidationException>(() =>
            UserManagementSupport.EnsureCanCreateRole(actor, targetRole, "roleId"));
    }

    [TestCase(Roles.ManagerSystemName)]
    [TestCase(Roles.AdminSystemName)]
    public void ManagerCannotCreateManagerOrAdminSystem(string targetSystemName)
    {
        var actor = UserWithRole(1, Roles.ManagerSystemName);
        var targetRole = Role(targetSystemName);

        Should.Throw<ValidationException>(() =>
            UserManagementSupport.EnsureCanCreateRole(actor, targetRole, "roleId"));
    }

    [Test]
    public void AdminCanCreateManager()
    {
        var actor = UserWithRole(1, Roles.AdminSystemName);
        var targetRole = Role(Roles.ManagerSystemName);

        Should.NotThrow(() => UserManagementSupport.EnsureCanCreateRole(actor, targetRole, "roleId"));
    }

    [TestCase(Roles.AdminSystemName)]
    [TestCase(Roles.CustomerSystemName)]
    [TestCase(Roles.StaffSystemName)]
    [TestCase(Roles.CustomerSystemName)]
    public void AdminCannotCreateNonManagerRoles(string targetSystemName)
    {
        var actor = UserWithRole(1, Roles.AdminSystemName);
        var targetRole = Role(targetSystemName);

        Should.Throw<ValidationException>(() =>
            UserManagementSupport.EnsureCanCreateRole(actor, targetRole, "roleId"));
    }

    [TestCase(Roles.CustomerSystemName)]
    [TestCase(Roles.StaffSystemName)]
    public void ManagerCanViewUpdateAndDeleteCustomerAndStaff(string targetSystemName)
    {
        var actor = UserWithRole(1, Roles.ManagerSystemName);
        var target = UserWithRole(2, targetSystemName);

        Should.NotThrow(() => UserManagementSupport.EnsureCanViewUser(actor, target));
        Should.NotThrow(() => UserManagementSupport.EnsureCanUpdateUser(actor, target));
        Should.NotThrow(() => UserManagementSupport.EnsureCanDeleteUser(actor, target));
    }

    [TestCase(Roles.ManagerSystemName)]
    [TestCase(Roles.AdminSystemName)]
    public void ManagerCannotViewUpdateOrDeleteManagerOrAdminSystem(string targetSystemName)
    {
        var actor = UserWithRole(1, Roles.ManagerSystemName);
        var target = UserWithRole(2, targetSystemName);

        Should.Throw<ForbiddenAccessException>(() => UserManagementSupport.EnsureCanViewUser(actor, target));
        Should.Throw<ForbiddenAccessException>(() => UserManagementSupport.EnsureCanUpdateUser(actor, target));
        Should.Throw<ForbiddenAccessException>(() => UserManagementSupport.EnsureCanDeleteUser(actor, target));
    }

    [TestCase(Roles.StaffSystemName)]
    public void ManagerCanAssignStaffRoleToCustomerOrStaff(string targetSystemName)
    {
        var actor = UserWithRole(1, Roles.ManagerSystemName);
        var target = UserWithRole(2, targetSystemName);

        Should.NotThrow(() =>
            UserManagementSupport.EnsureCanAssignRole(actor, target, Role(Roles.StaffSystemName), "roleId"));
    }

    [TestCase(Roles.CustomerSystemName)]
    [TestCase(Roles.StaffSystemName)]
    public void ManagerCannotAssignCustomerRole(string targetSystemName)
    {
        var actor = UserWithRole(1, Roles.ManagerSystemName);
        var target = UserWithRole(2, targetSystemName);

        Should.Throw<ValidationException>(() =>
            UserManagementSupport.EnsureCanAssignRole(actor, target, Role(Roles.CustomerSystemName), "roleId"));
    }

    [TestCase(Roles.ManagerSystemName)]
    [TestCase(Roles.AdminSystemName)]
    public void ManagerCannotAssignManagerOrAdminSystemRoles(string targetRoleSystemName)
    {
        var actor = UserWithRole(1, Roles.ManagerSystemName);
        var target = UserWithRole(2, Roles.StaffSystemName);

        Should.Throw<ValidationException>(() =>
            UserManagementSupport.EnsureCanAssignRole(actor, target, Role(targetRoleSystemName), "roleId"));
    }

    [TestCase(Roles.ManagerSystemName)]
    [TestCase(Roles.AdminSystemName)]
    public void ManagerCannotAssignRoleToManagerOrAdminSystemTarget(string targetSystemName)
    {
        var actor = UserWithRole(1, Roles.ManagerSystemName);
        var target = UserWithRole(2, targetSystemName);

        Should.Throw<ForbiddenAccessException>(() =>
            UserManagementSupport.EnsureCanAssignRole(actor, target, Role(Roles.CustomerSystemName), "roleId"));
    }

    [Test]
    public void AdminCanAssignManagerRole()
    {
        var actor = UserWithRole(1, Roles.AdminSystemName);
        var target = UserWithRole(2, Roles.StaffSystemName);

        Should.NotThrow(() =>
            UserManagementSupport.EnsureCanAssignRole(actor, target, Role(Roles.ManagerSystemName), "roleId"));
    }

    [TestCase(Roles.AdminSystemName)]
    [TestCase(Roles.StaffSystemName)]
    [TestCase(Roles.CustomerSystemName)]
    public void AdminCannotAssignNonManagerRoles(string targetRoleSystemName)
    {
        var actor = UserWithRole(1, Roles.AdminSystemName);
        var target = UserWithRole(2, Roles.StaffSystemName);

        Should.Throw<ValidationException>(() =>
            UserManagementSupport.EnsureCanAssignRole(actor, target, Role(Roles.AdminSystemName), "roleId"));
    }

    [Test]
    public void ActorCannotUpdateOrDeleteOwnAccountFromManagementApi()
    {
        var actor = UserWithRole(1, Roles.ManagerSystemName);
        var target = UserWithRole(1, Roles.StaffSystemName);

        Should.Throw<ValidationException>(() => UserManagementSupport.EnsureCanUpdateUser(actor, target));
        Should.Throw<ValidationException>(() => UserManagementSupport.EnsureCanDeleteUser(actor, target));
    }

    private static User UserWithRole(int id, string systemName) =>
        new()
        {
            Id = id,
            FullName = $"{systemName} User",
            Role = Role(systemName)
        };

    private static Role Role(string systemName) =>
        systemName switch
        {
            Roles.AdminSystemName => new Role
            {
                Id = 1,
                Code = Roles.AdminSystemCode,
                SystemName = Roles.AdminSystemName,
                DisplayName = "Admin System"
            },
            Roles.ManagerSystemName => new Role
            {
                Id = 2,
                Code = Roles.ManagerCode,
                SystemName = Roles.ManagerSystemName,
                DisplayName = "Manager"
            },
            Roles.StaffSystemName => new Role
            {
                Id = 3,
                Code = Roles.StaffCode,
                SystemName = Roles.StaffSystemName,
                DisplayName = "Staff"
            },
            Roles.CustomerSystemName => new Role
            {
                Id = 4,
                Code = Roles.CustomerCode,
                SystemName = Roles.CustomerSystemName,
                DisplayName = "Customer"
            },
            _ => throw new ArgumentOutOfRangeException(nameof(systemName), systemName, null)
        };
}
