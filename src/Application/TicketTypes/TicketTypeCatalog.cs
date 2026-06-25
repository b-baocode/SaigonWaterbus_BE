namespace SaigonWaterbus.Application.TicketTypes;

public sealed record TicketTypeDefinition(
    Guid TicketTypeId,
    string TicketTypeCode,
    string TicketTypeName,
    string? Description,
    decimal PriceModifier,
    int PointsEarnedRate,
    bool IsActive,
    int DisplayOrder,
    string Category);

public static class TicketTypeCatalog
{
    public const string CustomBookingTicketTypeCode = "CUSTOM_BOOKING";
    public const string CustomBookingTicketTypeName = "Vé thuê tàu";

    private static readonly IReadOnlyList<TicketTypeDefinition> Definitions =
    [
        new(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            "ADULT",
            "Vé người lớn",
            "Hành khách từ 12 tuổi trở lên",
            1.0m,
            10,
            true,
            1,
            "Age"),
        new(
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            "CHILD",
            "Vé trẻ em",
            "Trẻ em dưới 12 tuổi",
            0.5m,
            0,
            true,
            2,
            "Age"),
        new(
            Guid.Parse("33333333-3333-3333-3333-333333333333"),
            "SENIOR",
            "Vé người cao tuổi",
            "Hành khách từ 60 tuổi trở lên",
            0.5m,
            5,
            true,
            3,
            "Age"),
        new(
            Guid.Parse("44444444-4444-4444-4444-444444444444"),
            "STUDENT",
            "Vé học sinh / sinh viên",
            "Học sinh, sinh viên có xuất trình thẻ",
            0.8m,
            8,
            true,
            4,
            "Age")
    ];

    public static IReadOnlyList<TicketTypeDefinition> ActiveDefinitions =>
        Definitions
            .Where(x => x.IsActive)
            .OrderBy(x => x.DisplayOrder)
            .ThenBy(x => x.TicketTypeCode)
            .ToArray();

    public static TicketTypeDefinition? FindActiveByCode(string ticketTypeCode)
    {
        var code = NormalizeCode(ticketTypeCode);
        return ActiveDefinitions.FirstOrDefault(x => x.TicketTypeCode == code);
    }

    public static TicketTypeDefinition? FindActiveById(Guid ticketTypeId) =>
        ActiveDefinitions.FirstOrDefault(x => x.TicketTypeId == ticketTypeId);

    public static string NormalizeCode(string value) => value.Trim().ToUpperInvariant();
}
