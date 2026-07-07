using SaigonWaterbus.Domain.Enums;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Application.CharterBookings;

internal static class CharterBookingBoatSelectionSupport
{
    public const int MaxRequestedBoatCount = 20;

    public static IReadOnlyList<int> NormalizeRequestedBoatDecks(
        IReadOnlyList<CreateCharterBookingBoatRequest>? requestedBoats) =>
        requestedBoats is { Count: > 0 }
            ? requestedBoats.Select(x => x.NumberOfDecks).ToArray()
            : [];

    public static int ResolveRequestedBoatCount(IReadOnlyList<int> requestedBoatDecks) =>
        requestedBoatDecks.Count;

    public static string? ToStorageValue(IReadOnlyList<int> requestedBoatDecks) =>
        requestedBoatDecks.Count == 0
            ? null
            : string.Join(",", requestedBoatDecks);

    public static IReadOnlyList<int> FromDeckStorageValue(string? requestedBoatDecks)
    {
        if (string.IsNullOrWhiteSpace(requestedBoatDecks))
        {
            return [];
        }

        return requestedBoatDecks
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => int.TryParse(x, out var numberOfDecks) && numberOfDecks > 0)
            .Select(int.Parse)
            .ToArray();
    }

    public static IReadOnlyList<SeatSetupType> FromSeatSetupStorageValue(string? requestedBoatTypes)
    {
        if (string.IsNullOrWhiteSpace(requestedBoatTypes))
        {
            return [];
        }

        return requestedBoatTypes
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => Enum.TryParse<SeatSetupType>(x, ignoreCase: true, out _))
            .Select(x => Enum.Parse<SeatSetupType>(x, ignoreCase: true))
            .ToArray();
    }

    public static IReadOnlyList<CharterBookingRequestedBoatDto> ToDtos(
        string? requestedBoatDecks,
        string? requestedBoatTypes = null)
    {
        var deckCounts = FromDeckStorageValue(requestedBoatDecks);
        if (deckCounts.Count > 0)
        {
            return ToDtos(deckCounts);
        }

        return ToLegacySeatSetupDtos(FromSeatSetupStorageValue(requestedBoatTypes));
    }

    public static IReadOnlyList<CharterBookingRequestedBoatDto> ToDtos(
        IReadOnlyList<int> requestedBoatDecks) =>
        requestedBoatDecks
            .Select((numberOfDecks, index) => new CharterBookingRequestedBoatDto(index + 1, numberOfDecks))
            .ToArray();

    public static IReadOnlyList<CharterBookingRequestedBoatDto> ToLegacySeatSetupDtos(
        IReadOnlyList<SeatSetupType> requestedBoatTypes) =>
        requestedBoatTypes
            .Select((seatSetupType, index) => new CharterBookingRequestedBoatDto(
                index + 1,
                null,
                seatSetupType.ToString()))
            .ToArray();

    public static IReadOnlyList<CharterBookingSelectedBoatDto> ToSelectedBoatDtos(
        IEnumerable<CharterBookingBoat> selectedBoats) =>
        selectedBoats
            .OrderBy(x => x.BoatOrder)
            .Select(x => new CharterBookingSelectedBoatDto(
                x.BoatOrder,
                x.BoatId,
                x.Boat.Name,
                x.SeatSetupType.ToString(),
                x.Boat.NumberOfDecks,
                x.UnitPrice,
                x.ChargeableDurationValue,
                x.SubtotalAmount))
            .ToArray();

    public static IReadOnlyList<Guid> ResolveSelectedBoatIds(Booking booking)
    {
        var selectedBoatIds = booking.CharterBoats
            .OrderBy(x => x.BoatOrder)
            .Select(x => x.BoatId)
            .ToList();

        if (booking.BoatId.HasValue && !selectedBoatIds.Contains(booking.BoatId.Value))
        {
            selectedBoatIds.Insert(0, booking.BoatId.Value);
        }

        return selectedBoatIds
            .Distinct()
            .ToArray();
    }
}
