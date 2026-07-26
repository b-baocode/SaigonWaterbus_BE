using SaigonWaterbus.Domain.Constants;

namespace SaigonWaterbus.Application.TicketTypes;

public readonly record struct TicketTypeInfo(
    string Code,
    string Name,
    string? Description,
    decimal PriceModifier,
    string? AllowedSeatTypeCodes,
    decimal? SightseeingPriceModifier = null)
{
    public bool IsApplicableForSeatType(string seatTypeCode) =>
        AllowedSeatTypeCodes is null ||
        AllowedSeatTypeCodes.Split(',').Any(c =>
            c.Equals(seatTypeCode, StringComparison.OrdinalIgnoreCase));

    public IReadOnlyList<string>? GetAllowedSeatTypeCodesList() =>
        AllowedSeatTypeCodes?.Split(',', StringSplitOptions.RemoveEmptyEntries);

    public decimal GetPriceModifier(string? routeType) =>
        string.Equals(routeType, RouteTypes.SightseeingLoop, StringComparison.OrdinalIgnoreCase)
            ? SightseeingPriceModifier ?? PriceModifier
            : PriceModifier;
}

public static class TicketTypePricing
{
    public static readonly IReadOnlyList<TicketTypeInfo> All =
    [
        new("ADULT", "Vé người lớn", "Hành khách thông thường, nguyên giá", 1.0m, null),
        new("CHILD", "Vé trẻ em",
            "Giảm 50% giá vé. Áp dụng waterbus thường và sightseeing.",
            0.5m, null, 0.5m),
        new("INFANT", "Trẻ em dưới 2 tuổi",
            "Miễn phí. Có thể ngồi cùng người lớn (không chiếm ghế) hoặc chiếm 1 ghế riêng tùy khách. "
            + "Áp dụng waterbus thường và sightseeing.",
            0.0m, null, 0.0m),
        new("SENIOR", "Người cao tuổi trên 70",
            "Miễn phí, xuất trình giấy tờ tùy thân khi lên tàu. Chỉ áp dụng waterbus thường, không áp dụng sightseeing.",
            0.0m, "STANDARD"),
        new("DISABLED", "Người khuyết tật",
            "Miễn phí trên waterbus thường; giảm 50% trên sightseeing. Xuất trình thẻ/giấy xác nhận khi lên tàu.",
            0.0m, null, 0.5m)
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

    public static decimal GetMinimumPositivePriceModifier(string? routeType) =>
        All.Select(x => x.GetPriceModifier(routeType))
            .Where(x => x > 0)
            .Min();
}
