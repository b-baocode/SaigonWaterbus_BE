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
        new("ADULT", "Vé người lớn", "Hành khách thông thường, nguyên giá", 1.0m, null),
        new("INFANT", "Trẻ em dưới 2 tuổi",
            "Miễn phí. Có thể ngồi cùng người lớn (không chiếm ghế) hoặc chiếm 1 ghế riêng tùy khách. "
            + "Chỉ áp dụng waterbus thường, không áp dụng sightseeing.",
            0.0m, "STANDARD"),
        new("SENIOR", "Người cao tuổi trên 70",
            "Miễn phí, xuất trình giấy tờ tùy thân khi lên tàu. Chỉ áp dụng waterbus thường, không áp dụng sightseeing.",
            0.0m, "STANDARD"),
        new("DISABLED", "Người khuyết tật",
            "Miễn phí, xuất trình thẻ/giấy xác nhận khuyết tật khi lên tàu. Chỉ áp dụng waterbus thường, không áp dụng sightseeing.",
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
