using SaigonWaterbus.Domain.Enums;

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
}
