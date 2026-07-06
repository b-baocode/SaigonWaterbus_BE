using SaigonWaterbus.Domain.Enums;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Application.CharterBookings;

internal static class CharterBookingBoatSelectionSupport
{
    public const int MaxRequestedBoatCount = 20;

    public static IReadOnlyList<SeatSetupType> NormalizeRequestedBoatTypes(
        IReadOnlyList<CreateCharterBookingBoatRequest>? requestedBoats,
        SeatSetupType? preferredSeatSetupType)
    {
        if (requestedBoats is { Count: > 0 })
        {
            return requestedBoats
                .Select(x => x.SeatSetupType)
                .ToArray();
        }

        return preferredSeatSetupType.HasValue
            ? [preferredSeatSetupType.Value]
            : [];
    }

    public static SeatSetupType? FirstOrNull(IReadOnlyList<SeatSetupType> requestedBoatTypes) =>
        requestedBoatTypes.Count > 0 ? requestedBoatTypes[0] : null;

    public static string? ToStorageValue(IReadOnlyList<SeatSetupType> requestedBoatTypes) =>
        requestedBoatTypes.Count == 0
            ? null
            : string.Join(",", requestedBoatTypes.Select(x => x.ToString()));

    public static IReadOnlyList<SeatSetupType> FromStorageValue(string? requestedBoatTypes)
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

    public static IReadOnlyList<CharterBookingRequestedBoatDto> ToDtos(string? requestedBoatTypes)
    {
        if (string.IsNullOrWhiteSpace(requestedBoatTypes))
        {
            return [];
        }

        return requestedBoatTypes
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select((seatSetupType, index) => new CharterBookingRequestedBoatDto(index + 1, seatSetupType))
            .ToArray();
    }

    public static IReadOnlyList<CharterBookingRequestedBoatDto> ToDtos(
        IReadOnlyList<SeatSetupType> requestedBoatTypes) =>
        requestedBoatTypes
            .Select((seatSetupType, index) => new CharterBookingRequestedBoatDto(index + 1, seatSetupType.ToString()))
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
