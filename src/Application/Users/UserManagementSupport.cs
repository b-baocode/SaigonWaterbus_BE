using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Exceptions;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Application.Users;

internal static class UserManagementSupport
{
    public static IQueryable<User> BuildVisibleUsersQuery(IApplicationDbContext context, User actor)
    {
        var query = context.Set<User>()
            .Include(x => x.Role)
            .Include(x => x.StationAssignments).ThenInclude(a => a.Station)
            .AsQueryable();

        if (AuthSupport.IsAdmin(actor))
        {
            return query;
        }

        return query.Where(x =>
            x.Role.Code == Roles.CustomerCode
            || x.Role.Code == Roles.StaffCode);
    }

    public static void EnsureCanCreateRole(User actor, Role targetRole, string propertyName)
    {
        if (AuthSupport.IsAdmin(actor))
        {
            if (CanAdminManageRole(targetRole))
            {
                return;
            }

            throw AuthSupport.CreateValidationException(propertyName, "Admin chỉ được tạo tài khoản Manager.");
        }

        if (AuthSupport.IsManager(actor) && CanManagerManageRole(targetRole))
        {
            return;
        }

        throw AuthSupport.CreateValidationException(propertyName, "Manager chỉ được tạo tài khoản Staff. Customer phải dùng luồng đăng ký.");
    }

    public static void EnsureCanViewUser(User actor, User target)
    {
        if (AuthSupport.IsAdmin(actor))
        {
            return;
        }

        if (AuthSupport.IsManager(actor)
            && (AuthSupport.IsCustomer(target) || AuthSupport.IsStaff(target)))
        {
            return;
        }

        throw new ForbiddenAccessException();
    }

    public static void EnsureCanUpdateUser(User actor, User target)
    {
        if (actor.Id == target.Id)
        {
            throw AuthSupport.CreateValidationException(nameof(target.Id), "Không thể cập nhật tài khoản hiện tại bằng API quản lý. Vui lòng dùng API profile.");
        }

        if (AuthSupport.IsAdmin(actor))
        {
            return;
        }

        if (AuthSupport.IsManager(actor)
            && (AuthSupport.IsCustomer(target) || AuthSupport.IsStaff(target)))
        {
            return;
        }

        throw new ForbiddenAccessException();
    }

    public static void EnsureCanDeleteUser(User actor, User target)
    {
        if (actor.Id == target.Id)
        {
            throw AuthSupport.CreateValidationException(nameof(target.Id), "Không thể xóa tài khoản hiện tại bằng API quản lý. Vui lòng dùng API profile.");
        }

        if (AuthSupport.IsAdmin(actor)
            && (AuthSupport.IsManager(target) || AuthSupport.IsStaff(target)))
        {
            return;
        }

        if (AuthSupport.IsManager(actor)
            && (AuthSupport.IsCustomer(target) || AuthSupport.IsStaff(target)))
        {
            return;
        }

        throw new ForbiddenAccessException();
    }

    public static void EnsureCanResetManagedPassword(User actor, User target)
    {
        if (actor.Id == target.Id)
        {
            throw AuthSupport.CreateValidationException(nameof(target.Id), "Không thể sinh lại mật khẩu cho tài khoản hiện tại bằng API quản lý. Vui lòng dùng API đổi mật khẩu hoặc quên mật khẩu.");
        }

        if (AuthSupport.IsAdmin(actor)
            && (AuthSupport.IsManager(target) || AuthSupport.IsStaff(target)))
        {
            return;
        }

        if (AuthSupport.IsManager(actor) && AuthSupport.IsStaff(target))
        {
            return;
        }

        throw new ForbiddenAccessException();
    }

    public static void EnsureCanAssignRole(User actor, User target, Role targetRole, string propertyName)
    {
        if (AuthSupport.IsAdmin(actor))
        {
            if (CanAdminManageRole(targetRole))
            {
                return;
            }

            throw AuthSupport.CreateValidationException(propertyName, "Admin chỉ được gán vai trò Manager.");
        }

        if (AuthSupport.IsManager(actor))
        {
            if (!AuthSupport.IsCustomer(target) && !AuthSupport.IsStaff(target))
            {
                throw new ForbiddenAccessException();
            }

            if (!CanManagerManageRole(targetRole))
            {
                throw AuthSupport.CreateValidationException(propertyName, "Manager chỉ được gán vai trò Staff. Customer phải dùng luồng đăng ký.");
            }

            return;
        }

        throw new ForbiddenAccessException();
    }

    public static void EnsureCanManageStationAssignments(User actor)
    {
        if (AuthSupport.IsAdmin(actor))
        {
            return;
        }

        throw new ForbiddenAccessException();
    }

    public static void EnsureCanViewStationAssignments(User actor, User target)
    {
        if (AuthSupport.IsAdmin(actor) || actor.Id == target.Id)
        {
            return;
        }

        throw new ForbiddenAccessException();
    }

    public static void EnsureCanAssignStationsToUser(User actor, User target)
    {
        EnsureCanManageStationAssignments(actor);

        if (AuthSupport.IsManager(target) || AuthSupport.IsStaff(target))
        {
            return;
        }

        throw AuthSupport.CreateValidationException(nameof(target.Id), "Chỉ được gắn bến cho tài khoản Manager hoặc Staff.");
    }

    private static bool CanAdminManageRole(Role role) =>
        role.SystemName is Roles.ManagerSystemName;

    private static bool CanManagerManageRole(Role role) =>
        role.SystemName is Roles.StaffSystemName;

    public static async Task<User> GetVisibleUserByIdAsync(
        IApplicationDbContext context,
        User actor,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var user = await BuildVisibleUsersQuery(context, actor)
            .SingleOrDefaultAsync(x => x.Id == userId, cancellationToken)
            ?? throw new global::SaigonWaterbus.Application.Common.Exceptions.NotFoundException("Không tìm thấy user.");

        EnsureCanViewUser(actor, user);
        return user;
    }
}
