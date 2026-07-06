namespace SaigonWaterbus.Application.TicketTypes;

public readonly record struct TicketTypeInfo(
    string Code,
    string Name,
    string? Description,
    decimal PriceModifier,
    string? AllowedSeatTypeCodes)
{
    public bool IsApplicableForSeatType(string seatTypeCode) =>
        AllowedSeatTypeCodes is null ||
        AllowedSeatTypeCodes.Split(',').Any(c =>
            c.Equals(seatTypeCode, StringComparison.OrdinalIgnoreCase));

    public IReadOnlyList<string>? GetAllowedSeatTypeCodesList() =>
        AllowedSeatTypeCodes?.Split(',', StringSplitOptions.RemoveEmptyEntries);
}

public static class TicketTypePricing
{
    public static readonly IReadOnlyList<TicketTypeInfo> All =
    [
        new("ADULT", "Vé người lớn", "Hành khách từ 12 tuổi trở lên", 1.0m, null),
        new("CHILD", "Vé trẻ em", "Trẻ em dưới 12 tuổi", 0.5m, null),
        new("SENIOR", "Vé người cao tuổi", "Hành khách từ 60 tuổi trở lên", 0.5m, null),
        new("STUDENT", "Vé học sinh / sinh viên", "Học sinh, sinh viên có xuất trình thẻ", 0.8m, null),
        new("SPECIAL_POLICY", "Vé miễn phí (đối tượng chính sách)",
            "Người có công, người khuyết tật và đối tượng chính sách. Chỉ áp dụng dịch vụ waterbus thông thường.",
            0.0m, "STANDARD")
    ];

    public static bool TryGet(string? code, out TicketTypeInfo info)
    {
        if (code is not null)
        {
            var normalized = TicketTypeCatalog.NormalizeCode(code);
            foreach (var candidate in All)
            {
                if (candidate.Code == normalized)
                {
                    info = candidate;
                    return true;
                }
            }
        }

        info = default;
        return false;
    }
}
