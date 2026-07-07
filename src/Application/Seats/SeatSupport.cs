using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Exceptions;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Application.Seats;

public sealed record SeatTypeDto(Guid SeatTypeId, string SeatTypeCode, string SeatTypeName, decimal BasePrice);

public sealed record SeatDto(
    Guid SeatId,
    string SeatCode,
    SeatTypeDto? SeatType,
    int Deck,
    string Row,
    int Column,
    bool IsActive);

public sealed record SeatRowDto(string Row, IReadOnlyCollection<SeatDto> Seats);

public sealed record SeatLayoutCellDto(
    int Row,
    int Column,
    SeatLayoutCellType Type,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    SeatDto? Seat,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    SeatTypeDto? SeatType = null);

public sealed record SeatDeckDto(
    int DeckNumber,
    int? RowCount,
    int? ColumnCount,
    IReadOnlyCollection<SeatRowDto> Rows,
    IReadOnlyCollection<SeatLayoutCellDto> Cells);

public sealed record BoatSeatsDto(
    Guid BoatId,
    int TotalSeats,
    int ConfiguredSeats,
    int ActiveSeats,
    bool SeatsConfigured,
    IReadOnlyCollection<SeatDeckDto> Decks);

internal sealed record SeatDeckLayout(int DeckNumber, int RowCount, int ColumnCount);

internal static class SeatSupport
{
    public const string StandardSeatTypeName = "Standard";
    public const string CabinSeatTypeName = "Cabin";
    public const string RiverSeatTypeName = "River";
    public const string SkySeatTypeName = "Sky";

    public static async Task EnsureCurrentUserCanManageSeatsAsync(
        IApplicationDbContext context,
        IUserContext userContext,
        CancellationToken cancellationToken)
    {
        await AuthSupport.EnsureCurrentUserIsAdminAsync(context, userContext, cancellationToken);
    }

    public static async Task<User> EnsureCurrentUserCanViewSeatsAsync(
        IApplicationDbContext context,
        IUserContext userContext,
        CancellationToken cancellationToken)
    {
        var actor = await AuthSupport.GetCurrentUserWithRoleAsync(context, userContext, cancellationToken);
        if (!AuthSupport.IsAdmin(actor) && !AuthSupport.IsManager(actor) && !AuthSupport.IsStaff(actor))
            throw new ForbiddenAccessException();

        return actor;
    }

    public static string RowLabel(int index) =>
        ((char)('A' + index)).ToString();

    public static string SeatCode(int deck, string row, int column) =>
        $"{deck}-{row}{column}";

    public static SeatDto CreateSeatDto(Seat seat) =>
        new(
            seat.Id,
            seat.Code,
            BuildSeatTypeDto(seat),
            seat.Deck,
            seat.Row,
            seat.Column,
            seat.IsActive);

    public static BoatSeatsDto CreateBoatSeatsDto(
        Boat boat,
        IList<Seat> seats,
        IReadOnlyCollection<SeatDeckLayout>? deckLayouts = null)
    {
        var layoutsByDeck = (deckLayouts ?? [])
            .ToDictionary(x => x.DeckNumber);

        var seatsByDeck = seats
            .GroupBy(s => s.Deck)
            .ToDictionary(g => g.Key, g => g.ToArray());
        var deckNumbers = layoutsByDeck.Keys
            .Union(seatsByDeck.Keys)
            .OrderBy(deckNumber => deckNumber);

        var decks = deckNumbers
            .Select(deckNumber =>
            {
                layoutsByDeck.TryGetValue(deckNumber, out var layout);
                seatsByDeck.TryGetValue(deckNumber, out var deckSeats);
                deckSeats ??= [];
                var cells = CreateLayoutCellDtos(
                    layout,
                    deckSeats);

                return new SeatDeckDto(
                    deckNumber,
                    layout?.RowCount,
                    layout?.ColumnCount,
                    deckSeats
                    .GroupBy(s => s.Row)
                    .OrderBy(g => g.Key)
                    .Select(rowGroup => new SeatRowDto(
                        rowGroup.Key,
                        rowGroup
                            .OrderBy(s => s.Column)
                            .Select(CreateSeatDto)
                            .ToArray()))
                    .ToArray(),
                    cells);
            })
            .ToArray();

        var activeSeats = seats.Count(s => s.IsActive);

        return new BoatSeatsDto(
            boat.Id,
            boat.SeatCount,
            seats.Count,
            activeSeats,
            boat.SeatsConfigured || (boat.SeatCount > 0 && seats.Count == boat.SeatCount),
            decks);
    }

    public static string NormalizeSeatTypeName(string? seatTypeCode, SeatSetupType seatSetupType) =>
        SeatTypeNameFromCode(NormalizeSeatTypeCode(seatTypeCode, seatSetupType));

    public static string NormalizeSeatTypeCode(
        string? seatTypeCode,
        SeatSetupType seatSetupType,
        IReadOnlyCollection<string>? extraAllowedCodes = null)
    {
        var code = string.IsNullOrWhiteSpace(seatTypeCode)
            ? DefaultSeatTypeCode(seatSetupType)
            : seatTypeCode.Trim().Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant();

        EnsureSeatTypeAllowed(code, seatSetupType, extraAllowedCodes);
        return code;
    }

    private static string SeatTypeNameFromCode(string seatTypeCode) =>
        seatTypeCode switch
        {
            "STANDARD" => StandardSeatTypeName,
            "CABIN" => CabinSeatTypeName,
            "RIVER" => RiverSeatTypeName,
            "SKY" => SkySeatTypeName,
            _ => throw AuthSupport.CreateValidationException("seatTypeCode", "Loại ghế chỉ được là STANDARD, CABIN, RIVER hoặc SKY.")
        };

    private static void EnsureSeatTypeAllowed(
        string seatTypeCode,
        SeatSetupType seatSetupType,
        IReadOnlyCollection<string>? extraAllowedCodes = null)
    {
        if (seatSetupType == SeatSetupType.FullStandard)
        {
            if (seatTypeCode != "STANDARD")
            {
                throw AuthSupport.CreateValidationException("seatTypeCode",
                    "Tàu FullStandard chỉ được dùng ghế STANDARD.");
            }

            return;
        }

        // Tàu sightseeing (StandardAndVip): 3 loại built-in + loại ghế tùy chỉnh admin đã tạo trong seat_types.
        var builtInCodes = new[] { "CABIN", "RIVER", "SKY" };
        if (builtInCodes.Contains(seatTypeCode))
        {
            return;
        }

        if (extraAllowedCodes is not null
            && seatTypeCode != "STANDARD"
            && extraAllowedCodes.Contains(seatTypeCode, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        throw AuthSupport.CreateValidationException("seatTypeCode",
            "Tàu StandardAndVip chỉ được dùng ghế CABIN, RIVER, SKY hoặc loại ghế tùy chỉnh đã tạo trong /api/seat-types.");
    }

    private static string DefaultSeatTypeCode(SeatSetupType seatSetupType) =>
        seatSetupType == SeatSetupType.StandardAndVip ? "CABIN" : "STANDARD";

    private static IReadOnlyCollection<SeatLayoutCellDto> CreateLayoutCellDtos(
        SeatDeckLayout? layout,
        IReadOnlyCollection<Seat> seats)
    {
        var seatByCell = seats.ToDictionary(
            seat => (Row: RowIndex(seat.Row), seat.Column));

        var rowCount = layout?.RowCount
            ?? new[]
            {
                seatByCell.Keys.Select(x => x.Row).DefaultIfEmpty(0).Max()
            }.Max();
        var columnCount = layout?.ColumnCount
            ?? new[]
            {
                seatByCell.Keys.Select(x => x.Column).DefaultIfEmpty(0).Max()
            }.Max();

        if (rowCount <= 0 || columnCount <= 0)
        {
            return [];
        }

        var defaultOpenCellType = seats.Count > 0
            ? SeatLayoutCellType.Aisle
            : SeatLayoutCellType.Empty;
        var cells = new List<SeatLayoutCellDto>(rowCount * columnCount);
        for (var row = 1; row <= rowCount; row++)
        {
            for (var column = 1; column <= columnCount; column++)
            {
                var key = (Row: row, Column: column);
                if (seatByCell.TryGetValue(key, out var seat))
                {
                    var seatDto = CreateSeatDto(seat);
                    cells.Add(new SeatLayoutCellDto(
                        row,
                        column,
                        SeatLayoutCellType.Seat,
                        seatDto,
                        seatDto.SeatType));
                    continue;
                }

                var cellType = defaultOpenCellType;
                cells.Add(new SeatLayoutCellDto(
                    row,
                    column,
                    cellType,
                    null));
            }
        }

        return cells;
    }

    public static SeatTypeDto BuildSeatTypeDto(Seat seat) =>
        seat.SeatType is { } st
            ? new(st.Id, st.Code, st.Name, st.BasePrice)
            : BuildSeatTypeDtoFromCode(seat.SeatTypeCode);

    public static SeatTypeDto BuildSeatTypeDtoFromCode(string seatTypeCode) =>
        new(Guid.Empty, seatTypeCode, SeatTypeNameFromCode(seatTypeCode), SeatTypePricing.GetBasePrice(seatTypeCode));

    public static decimal GetBasePrice(Seat seat) =>
        seat.SeatType?.BasePrice ?? SeatTypePricing.GetBasePrice(seat.SeatTypeCode);

    private static int RowIndex(string row) =>
        string.IsNullOrWhiteSpace(row)
            ? 0
            : char.ToUpperInvariant(row[0]) - 'A' + 1;
}
