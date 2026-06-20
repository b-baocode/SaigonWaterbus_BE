using SaigonWaterbus.Application.Auth.Common;

namespace SaigonWaterbus.Application.CustomBookingRequests;

internal static class CustomBookingRefundAccountValidation
{
    public const int BankBinLength = 6;
    public const int MaxAccountNumberLength = 50;
    public const int MaxAccountNameLength = 150;

    public static bool IsValidBankBin(string? value) =>
        IsAsciiDigits(value, BankBinLength, BankBinLength);

    public static bool IsValidAccountNumber(string? value) =>
        IsAsciiDigits(value, 1, MaxAccountNumberLength);

    public static string NormalizeRequiredBankBin(string? value, string propertyName)
    {
        var normalized = NormalizeRequired(value, propertyName);
        if (!IsValidBankBin(normalized))
        {
            throw AuthSupport.CreateValidationException(
                propertyName,
                "Mã BIN ngân hàng nhận hoàn tiền phải gồm đúng 6 chữ số theo chuẩn PayOS.");
        }

        return normalized;
    }

    public static string NormalizeRequiredAccountNumber(string? value, string propertyName)
    {
        var normalized = NormalizeRequired(value, propertyName);
        if (!IsValidAccountNumber(normalized))
        {
            throw AuthSupport.CreateValidationException(
                propertyName,
                "Số tài khoản nhận hoàn tiền chỉ được gồm chữ số và không vượt quá 50 ký tự.");
        }

        return normalized;
    }

    public static string NormalizeRequiredAccountName(string? value, string propertyName)
    {
        var normalized = NormalizeRequired(value, propertyName);
        if (normalized.Length > MaxAccountNameLength)
        {
            throw AuthSupport.CreateValidationException(
                propertyName,
                "Tên tài khoản nhận hoàn tiền không được vượt quá 150 ký tự.");
        }

        return normalized;
    }

    public static string RequiredRefundAccountMessage =>
        "Thông tin tài khoản nhận hoàn tiền là bắt buộc khi booking đủ điều kiện hoàn tiền.";

    private static string NormalizeRequired(string? value, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw AuthSupport.CreateValidationException(propertyName, RequiredRefundAccountMessage);
        }

        return value.Trim();
    }

    private static bool IsAsciiDigits(string? value, int minLength, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        return trimmed.Length >= minLength
            && trimmed.Length <= maxLength
            && trimmed.All(static c => c is >= '0' and <= '9');
    }
}
