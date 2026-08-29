using System.Text.RegularExpressions;

namespace SaigonWaterbus.Application.Stations;

internal static partial class StationInputValidationSupport
{
    public const string InvalidCodeMessage =
        "Mã nhà ga chỉ được chứa chữ cái, chữ số và dấu gạch ngang (-); dấu - không được ở đầu, cuối hoặc lặp liên tiếp.";

    public const string InvalidNameMessage =
        "Tên nhà ga chỉ được chứa chữ cái, chữ số và khoảng trắng.";

    public static bool IsValidCode(string? value) =>
        !string.IsNullOrWhiteSpace(value) && StationCodeRegex().IsMatch(value.Trim());

    public static bool IsValidName(string? value) =>
        !string.IsNullOrWhiteSpace(value) && StationNameRegex().IsMatch(value.Trim());

    public static string NormalizeName(string value) =>
        string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    [GeneratedRegex("^[A-Za-z0-9]+(?:-[A-Za-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex StationCodeRegex();

    [GeneratedRegex(@"^[\p{L}\p{M}\p{N}]+(?: +[\p{L}\p{M}\p{N}]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex StationNameRegex();
}
