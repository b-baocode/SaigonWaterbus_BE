using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.CharterBookings;

internal static class CharterBookingDuplicateSupport
{
    private static readonly BookingStatus[] ActiveStatuses =
    [
        BookingStatus.PendingQuote,
        BookingStatus.Quoted,
        BookingStatus.PendingApproval,
        BookingStatus.Approved,
        BookingStatus.PendingPayment,
        BookingStatus.Confirmed
    ];

    public static async Task EnsureNoDuplicateActiveRequestAsync(
        IApplicationDbContext context,
        Guid userId,
        Guid? excludeBookingId,
        DateOnly departureDate,
        TimeOnly? startTime,
        BoatRentalUnit? rentalUnit,
        int? durationValue,
        Guid? fromStationId,
        Guid? toStationId,
        int adultCount,
        int childCount,
        string? requestedBoatDecks,
        IReadOnlyList<CharterBookingDuplicateItineraryStop> itineraryStops,
        string contactPhone,
        string? contactEmail,
        CancellationToken cancellationToken)
    {
        var candidates = await CharterBookingQuerySupport.BuildBaseQuery(context)
            .AsNoTracking()
            .Include(x => x.ItineraryStops)
            .Where(x => (!excludeBookingId.HasValue || x.Id != excludeBookingId.Value)
                && ActiveStatuses.Contains(x.BookingStatus)
                && x.DepartureDate == departureDate
                && x.StartTime == startTime
                && x.RentalUnit == rentalUnit
                && x.DurationValue == durationValue
                && x.FromStationId == fromStationId
                && x.ToStationId == toStationId
                && x.AdultCount == adultCount
                && x.ChildCount == childCount
                && x.RequestedBoatDecks == requestedBoatDecks)
            .ToListAsync(cancellationToken);

        var normalizedPhone = NormalizePhone(contactPhone);
        var normalizedEmail = NormalizeEmail(contactEmail);
        var duplicate = candidates.FirstOrDefault(candidate =>
            IsSameRequester(candidate, userId, normalizedPhone, normalizedEmail)
            && HasSameItinerary(ToItineraryStops(candidate.ItineraryStops), itineraryStops));

        if (duplicate is null)
        {
            return;
        }

        throw new ValidationException([new ValidationFailure(
            "duplicateBooking",
            $"Hiện đã có một yêu cầu thuê tàu trùng ngày, giờ và lộ trình. Mã yêu cầu: {duplicate.BookingCode}. Vui lòng kiểm tra yêu cầu hiện có hoặc chỉnh sửa yêu cầu đó.")]);
    }

    public static IReadOnlyList<CharterBookingDuplicateItineraryStop> ToItineraryStops(
        IReadOnlyList<CreateCharterBookingItineraryStopRequest>? stops) =>
        stops?
            .OrderBy(x => x.StopOrder)
            .Select(x => new CharterBookingDuplicateItineraryStop(
                x.StationId,
                x.StopOrder,
                x.StayDurationMinutes))
            .ToArray() ?? [];

    public static IReadOnlyList<CharterBookingDuplicateItineraryStop> ToItineraryStops(
        IEnumerable<BookingItineraryStop> stops) =>
        stops
            .OrderBy(x => x.StopOrder)
            .Select(x => new CharterBookingDuplicateItineraryStop(
                x.StationId,
                x.StopOrder,
                x.StayDurationMinutes))
            .ToArray();

    private static bool IsSameRequester(
        Booking candidate,
        Guid userId,
        string normalizedPhone,
        string normalizedEmail) =>
        candidate.UserId == userId
        || (!string.IsNullOrEmpty(normalizedPhone)
            && NormalizePhone(candidate.ContactPhone) == normalizedPhone)
        || (!string.IsNullOrEmpty(normalizedEmail)
            && NormalizeEmail(candidate.ContactEmail) == normalizedEmail);

    private static bool HasSameItinerary(
        IReadOnlyList<CharterBookingDuplicateItineraryStop> candidateStops,
        IReadOnlyList<CharterBookingDuplicateItineraryStop> requestedStops)
    {
        if (candidateStops.Count != requestedStops.Count)
        {
            return false;
        }

        for (var i = 0; i < candidateStops.Count; i++)
        {
            if (candidateStops[i] != requestedStops[i])
            {
                return false;
            }
        }

        return true;
    }

    private static string NormalizePhone(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : new string(value.Where(char.IsDigit).ToArray());

    private static string NormalizeEmail(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim().ToUpperInvariant();
}

internal readonly record struct CharterBookingDuplicateItineraryStop(
    Guid StationId,
    int StopOrder,
    int StayDurationMinutes);
