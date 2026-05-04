namespace SaigonWaterbus.Domain.Constants;

public static class PasswordRules
{
    public const int MinimumLength = 8;

    public const string StrongPasswordMessage =
        "Mật khẩu phải có ít nhất 8 ký tự, gồm chữ hoa, chữ thường và ký tự đặc biệt.";

    public static bool IsStrong(string? password)
    {
        if (string.IsNullOrWhiteSpace(password) || password.Length < MinimumLength)
        {
            return false;
        }

        return password.Any(char.IsUpper)
            && password.Any(char.IsLower)
            && password.Any(IsSpecialCharacter);
    }

    private static bool IsSpecialCharacter(char value) =>
        !char.IsLetterOrDigit(value);
}
