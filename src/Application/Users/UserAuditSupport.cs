using System.Text.Json;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Application.Users;

internal static class UserAuditSupport
{
    private const string UsersTargetTable = "users";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static AuditLog CreateUserAuditLog(
        int actorUserId,
        string action,
        int targetUserId,
        object? oldValues,
        object? newValues,
        DateTimeOffset createdAt) =>
        new()
        {
            ActorUserId = actorUserId,
            Action = action,
            TargetTable = UsersTargetTable,
            TargetId = targetUserId,
            OldValues = Serialize(oldValues),
            NewValues = Serialize(newValues),
            CreatedAt = createdAt
        };

    public static object CreateUserSnapshot(User user, Role? role = null) =>
        new
        {
            user.Id,
            user.UserCode,
            user.FullName,
            user.DateOfBirth,
            user.PhoneNumber,
            user.Email,
            user.RoleId,
            RoleSystemName = role?.SystemName ?? user.Role?.SystemName,
            Status = user.Status.ToString(),
            user.PhoneVerifiedAt,
            user.LastLoginAt
        };

    private static string? Serialize(object? values) =>
        values is null ? null : JsonSerializer.Serialize(values, JsonOptions);
}
