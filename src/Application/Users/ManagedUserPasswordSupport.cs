using System.Security.Cryptography;
using SaigonWaterbus.Domain.Constants;

namespace SaigonWaterbus.Application.Users;

internal static class ManagedUserPasswordSupport
{
    private const int GeneratedPasswordLength = 12;
    private const string UppercaseCharacters = "ABCDEFGHJKLMNPQRSTUVWXYZ";
    private const string LowercaseCharacters = "abcdefghijkmnopqrstuvwxyz";
    private const string DigitCharacters = "23456789";
    private const string SpecialCharacters = "!@#$%^&*";

    private static readonly char[] AllCharacters =
        (UppercaseCharacters + LowercaseCharacters + DigitCharacters + SpecialCharacters).ToCharArray();

    public static string GeneratePassword()
    {
        Span<char> password = stackalloc char[GeneratedPasswordLength];
        password[0] = Pick(UppercaseCharacters);
        password[1] = Pick(LowercaseCharacters);
        password[2] = Pick(SpecialCharacters);
        password[3] = Pick(DigitCharacters);

        for (var i = 4; i < password.Length; i++)
        {
            password[i] = Pick(AllCharacters);
        }

        Shuffle(password);
        var generatedPassword = new string(password);
        if (!PasswordRules.IsStrong(generatedPassword))
        {
            throw new InvalidOperationException("Generated password does not satisfy password rules.");
        }

        return generatedPassword;
    }

    private static char Pick(string characters) =>
        characters[RandomNumberGenerator.GetInt32(characters.Length)];

    private static char Pick(char[] characters) =>
        characters[RandomNumberGenerator.GetInt32(characters.Length)];

    private static void Shuffle(Span<char> values)
    {
        for (var i = values.Length - 1; i > 0; i--)
        {
            var j = RandomNumberGenerator.GetInt32(i + 1);
            (values[i], values[j]) = (values[j], values[i]);
        }
    }
}
