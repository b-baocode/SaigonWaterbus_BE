using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Exceptions;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Application.Seats;

public sealed record SeatDto(Guid Id, string Code, int Deck, string Row, int Column, bool IsActive);

public sealed record SeatRowDto(string Row, IReadOnlyCollection<SeatDto> Seats);

public sealed record SeatDeckDto(int DeckNumber, int? RowCount, int? ColumnCount, IReadOnlyCollection<SeatRowDto> Rows);

public sealed record VesselFacilityDto(
    Guid Id,
    VesselFacilityType Type,
    int Deck,
    string Row,
    int Column,
    int RowSpan,
    int ColumnSpan,
    bool IsActive);

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

    public static VesselSeatsDto CreateVesselSeatsDto(
        Vessel vessel,
        IList<Seat> seats,
        IList<VesselDeckLayout>? deckLayouts = null,
        IList<VesselFacility>? facilities = null)
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
                            .Select(s => new SeatDto(s.Id, s.Code, s.Deck, s.Row, s.Column, s.IsActive))
                            .ToArray()))
                    .ToArray());
            })
            .ToArray();

        var facilityDtos = (facilities ?? [])
            .OrderBy(f => f.Deck)
            .ThenBy(f => f.Row)
            .ThenBy(f => f.Column)
            .Select(f => new VesselFacilityDto(f.Id, f.Type, f.Deck, f.Row, f.Column, f.RowSpan, f.ColumnSpan, f.IsActive))
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
}
