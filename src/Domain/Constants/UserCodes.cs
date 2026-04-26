namespace SaigonWaterbus.Domain.Constants;

public static class UserCodes
{
    public const string CustomerPrefix = "CU";
    public const string ManagerPrefix = "MG";
    public const string AdminPrefix = "AD";
    public const string StaffPrefix = "ST";
    public const int NumberWidth = 7;
    public const int MaxNumber = 9_999_999;

    public static string GetPrefixForRoleCode(string roleCode) => roleCode switch
    {
        Roles.CustomerCode => CustomerPrefix,
        Roles.ManagerCode => ManagerPrefix,
        Roles.AdminSystemCode => AdminPrefix,
        Roles.StaffCode => StaffPrefix,
        _ => throw new ArgumentOutOfRangeException(nameof(roleCode), roleCode, "Role code does not support user codes.")
    };

    public static string GetSequenceName(string prefix) => $"user_code_{prefix.ToLowerInvariant()}_seq";

    public static string Format(string prefix, int number)
    {
        if (number < 1 || number > MaxNumber)
        {
            throw new InvalidOperationException($"User code number for prefix '{prefix}' is out of range.");
        }

        return $"{prefix}{number:D7}";
    }

    public static bool HasPrefix(string? userCode, string prefix) =>
        !string.IsNullOrWhiteSpace(userCode)
        && userCode.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
}
