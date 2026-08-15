using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Application.Trips;

/// <summary>
/// Map danh sách hành khách của charter booking sang DTO gọn nhẹ cho trip detail.
/// Mỗi passenger có thể gắn với ticket của riêng họ; lấy ticket đầu tiên (charter thường
/// là 1 vé / khách).
/// </summary>
internal static class CharterTripPassengerMapper
{
    public static IReadOnlyList<CharterTripPassengerInfoDto> FromBooking(Booking booking)
    {
        if (booking.Passengers is null || booking.Passengers.Count == 0)
        {
            return [];
        }

        return booking.Passengers
            .OrderBy(p => p.FullName, StringComparer.OrdinalIgnoreCase)
            .Select(p =>
            {
                var ticket = p.Tickets?.FirstOrDefault();
                return new CharterTripPassengerInfoDto(
                    p.Id,
                    p.FullName,
                    p.PhoneNumber,
                    p.Email,
                    p.DateOfBirth,
                    p.BirthYear,
                    p.Gender,
                    p.PassengerType,
                    p.Nationality,
                    ticket?.Id,
                    ticket?.TicketCode,
                    ticket?.TicketStatus.ToString(),
                    ticket?.CheckedInAt,
                    ticket?.CheckedOutAt);
            })
            .ToList();
    }
}