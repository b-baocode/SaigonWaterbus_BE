using System.Globalization;
using System.Text;

namespace SaigonWaterbus.Application.Common;

/// <summary>
/// Chuẩn hoá text tiếng Việt để so khớp khoan dung với cách gõ của khách (bỏ dấu + lowercase).
/// Dùng chung cho việc khớp tên ga của trợ lý ảo và tìm kiếm knowledge base — đừng viết bản
/// thứ hai ở nơi khác, hai chỗ lệch nhau là sinh bug khó thấy.
/// </summary>
public static class VietnameseTextSupport
{
    /// <summary>Bỏ dấu tiếng Việt, hạ chữ thường, đổi "đ" thành "d".</summary>
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var formD = value.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(formD.Length);
        foreach (var c in formD)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                sb.Append(char.ToLowerInvariant(c));
            }
        }

        return sb.ToString()
            .Replace("đ", "d", StringComparison.Ordinal)
            .Trim();
    }
}
