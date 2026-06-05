using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Exceptions;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Application.Seats;

public sealed record SeatDto(int Id, string Code, int Deck, string Row, int Column, bool IsActive);

public sealed record SeatRowDto(string Row, IReadOnlyCollection<SeatDto> Seats);

public sealed record SeatDeckDto(int DeckNumber, IReadOnlyCollection<SeatRowDto> Rows);

public sealed record VesselSeatsDto(
    int VesselId,
    string VesselCode,
    string VesselName,
    int TotalSeats,
    int ConfiguredSeats,
    int ActiveSeats,
    bool SeatsConfigured,
    IReadOnlyCollection<SeatDeckDto> Decks);

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

    public static VesselSeatsDto CreateVesselSeatsDto(Vessel vessel, IList<Seat> seats)
    {
        var decks = seats
            .GroupBy(s => s.Deck)
            .OrderBy(g => g.Key)
            .Select(deckGroup => new SeatDeckDto(
                deckGroup.Key,
                deckGroup
                    .GroupBy(s => s.Row)
                    .OrderBy(g => g.Key)
                    .Select(rowGroup => new SeatRowDto(
                        rowGroup.Key,
                        rowGroup
                            .OrderBy(s => s.Column)
                            .Select(s => new SeatDto(s.Id, s.Code, s.Deck, s.Row, s.Column, s.IsActive))
                            .ToArray()))
                    .ToArray()))
            .ToArray();

        var activeSeats = seats.Count(s => s.IsActive);

        return new VesselSeatsDto(
            vessel.Id,
            vessel.Code,
            vessel.Name,
            vessel.SeatCount,
            seats.Count,
            activeSeats,
            vessel.SeatsConfigured,
            decks);
    }
}
