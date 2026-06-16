using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Exceptions;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Application.Seats;

public sealed record SeatTypeDto(Guid SeatTypeId, string SeatTypeCode, string SeatTypeName);

public sealed record SeatDto(
    Guid SeatId,
    string SeatCode,
    SeatTypeDto? SeatType,
    int Deck,
    string Row,
    int Column,
    bool IsActive);

public sealed record SeatRowDto(string Row, IReadOnlyCollection<SeatDto> Seats);

public sealed record VesselFacilityDto(
    Guid Id,
    VesselFacilityType Type,
    int Deck,
    string Row,
    int Column,
    int RowSpan,
    int ColumnSpan,
    bool IsActive);

public sealed record SeatLayoutCellDto(
    int Row,
    int Column,
    SeatLayoutCellType Type,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    SeatDto? Seat,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    VesselFacilityDto? Facility);

public sealed record SeatDeckDto(
    int DeckNumber,
    int? RowCount,
    int? ColumnCount,
    IReadOnlyCollection<SeatRowDto> Rows,
    IReadOnlyCollection<SeatLayoutCellDto> Cells);

public sealed record VesselSeatsDto(
    Guid VesselId,
    int TotalSeats,
    int ConfiguredSeats,
    int ActiveSeats,
    bool SeatsConfigured,
    IReadOnlyCollection<SeatDeckDto> Decks,
    IReadOnlyCollection<VesselFacilityDto> Facilities);

internal static class SeatSupport
{
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
            seat.SeatType is null ? null : new SeatTypeDto(seat.SeatType.Id, seat.SeatType.Code, seat.SeatType.Name),
            seat.Deck,
            seat.Row,
            seat.Column,
            seat.IsActive);

    public static VesselSeatsDto CreateVesselSeatsDto(
        Vessel vessel,
        IList<Seat> seats,
        IList<VesselDeckLayout>? deckLayouts = null,
        IList<VesselFacility>? facilities = null,
        IList<VesselLayoutCell>? layoutCells = null)
    {
        var layoutsByDeck = (deckLayouts ?? [])
            .ToDictionary(x => x.DeckNumber);

        var seatsByDeck = seats
            .GroupBy(s => s.Deck)
            .ToDictionary(g => g.Key, g => g.ToArray());
        var facilitiesByDeck = (facilities ?? [])
            .GroupBy(f => f.Deck)
            .ToDictionary(g => g.Key, g => g.ToArray());
        var layoutCellsByDeck = (layoutCells ?? [])
            .GroupBy(c => c.Deck)
            .ToDictionary(g => g.Key, g => g.ToArray());
        var deckNumbers = layoutsByDeck.Keys
            .Union(seatsByDeck.Keys)
            .Union(facilitiesByDeck.Keys)
            .Union(layoutCellsByDeck.Keys)
            .OrderBy(deckNumber => deckNumber);

        var decks = deckNumbers
            .Select(deckNumber =>
            {
                layoutsByDeck.TryGetValue(deckNumber, out var layout);
                seatsByDeck.TryGetValue(deckNumber, out var deckSeats);
                deckSeats ??= [];
                facilitiesByDeck.TryGetValue(deckNumber, out var deckFacilities);
                deckFacilities ??= [];
                layoutCellsByDeck.TryGetValue(deckNumber, out var deckLayoutCells);
                deckLayoutCells ??= [];
                var cells = CreateLayoutCellDtos(layout, deckSeats, deckFacilities, deckLayoutCells);

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

        var facilityDtos = (facilities ?? [])
            .OrderBy(f => f.Deck)
            .ThenBy(f => f.Row)
            .ThenBy(f => f.Column)
            .Select(CreateFacilityDto)
            .ToArray();

        var activeSeats = seats.Count(s => s.IsActive);

        return new VesselSeatsDto(
            vessel.Id,
            vessel.SeatCount,
            seats.Count,
            activeSeats,
            vessel.SeatsConfigured,
            decks,
            facilityDtos);
    }

    private static IReadOnlyCollection<SeatLayoutCellDto> CreateLayoutCellDtos(
        VesselDeckLayout? layout,
        IReadOnlyCollection<Seat> seats,
        IReadOnlyCollection<VesselFacility> facilities,
        IReadOnlyCollection<VesselLayoutCell> layoutCells)
    {
        var seatByCell = seats.ToDictionary(
            seat => (Row: RowIndex(seat.Row), seat.Column));
        var facilityByCell = facilities
            .SelectMany(facility => FacilityCells(facility).Select(cell => (cell.Row, cell.Column, Facility: facility)))
            .ToDictionary(x => (x.Row, x.Column), x => x.Facility);
        var layoutCellByCell = layoutCells.ToDictionary(
            cell => (Row: RowIndex(cell.Row), cell.Column),
            cell => cell.Type);

        var rowCount = layout?.RowCount
            ?? new[]
            {
                seatByCell.Keys.Select(x => x.Row).DefaultIfEmpty(0).Max(),
                facilityByCell.Keys.Select(x => x.Row).DefaultIfEmpty(0).Max(),
                layoutCellByCell.Keys.Select(x => x.Row).DefaultIfEmpty(0).Max()
            }.Max();
        var columnCount = layout?.ColumnCount
            ?? new[]
            {
                seatByCell.Keys.Select(x => x.Column).DefaultIfEmpty(0).Max(),
                facilityByCell.Keys.Select(x => x.Column).DefaultIfEmpty(0).Max(),
                layoutCellByCell.Keys.Select(x => x.Column).DefaultIfEmpty(0).Max()
            }.Max();

        if (rowCount <= 0 || columnCount <= 0)
        {
            return [];
        }

        var defaultOpenCellType = seats.Count > 0 && layoutCells.Count == 0
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
                    cells.Add(new SeatLayoutCellDto(
                        row,
                        column,
                        SeatLayoutCellType.Seat,
                        CreateSeatDto(seat),
                        null));
                    continue;
                }

                if (facilityByCell.TryGetValue(key, out var facility))
                {
                    cells.Add(new SeatLayoutCellDto(
                        row,
                        column,
                        SeatLayoutCellType.Toilet,
                        null,
                        CreateFacilityDto(facility)));
                    continue;
                }

                var cellType = layoutCellByCell.TryGetValue(key, out var explicitCellType)
                    ? ToSeatLayoutCellType(explicitCellType)
                    : defaultOpenCellType;
                cells.Add(new SeatLayoutCellDto(row, column, cellType, null, null));
            }
        }

        return cells;
    }

    private static IEnumerable<(int Row, int Column)> FacilityCells(VesselFacility facility)
    {
        var rowIndex = RowIndex(facility.Row);
        for (var row = rowIndex; row < rowIndex + facility.RowSpan; row++)
        {
            for (var column = facility.Column; column < facility.Column + facility.ColumnSpan; column++)
            {
                yield return (row, column);
            }
        }
    }

    private static int RowIndex(string row) =>
        string.IsNullOrWhiteSpace(row)
            ? 0
            : char.ToUpperInvariant(row[0]) - 'A' + 1;

    private static SeatLayoutCellType ToSeatLayoutCellType(VesselLayoutCellType type) =>
        type switch
        {
            VesselLayoutCellType.Aisle => SeatLayoutCellType.Aisle,
            _ => SeatLayoutCellType.Empty
        };

    private static VesselFacilityDto CreateFacilityDto(VesselFacility facility) =>
        new(
            facility.Id,
            facility.Type,
            facility.Deck,
            facility.Row,
            facility.Column,
            facility.RowSpan,
            facility.ColumnSpan,
            facility.IsActive);
}
