namespace SaigonWaterbus.Application.Users;

internal static class UserInputValidationSupport
{
    public const string InvalidFullNameMessage =
        "Họ và tên chỉ được chứa chữ cái và khoảng trắng.";

    public static bool IsValidFullName(string? fullName) =>
        !string.IsNullOrWhiteSpace(fullName)
        && fullName.Trim().All(character => char.IsLetter(character) || character == ' ');
}
