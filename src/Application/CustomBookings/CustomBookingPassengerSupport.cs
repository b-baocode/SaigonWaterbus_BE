using System.Globalization;
using FluentValidation.Results;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.CustomBookings;

internal static class CustomBookingPassengerSupport
{
    public const int AdultMinimumAge = 12;
    private static readonly string[] DateFormats =
    [
        "yyyy-MM-dd",
        "yyyy/MM/dd",
        "dd/MM/yyyy",
        "d/M/yyyy",
        "dd-MM-yyyy",
        "d-M-yyyy",
        "dd.MM.yyyy",
        "d.M.yyyy",
        "MM/dd/yyyy",
        "M/d/yyyy"
    ];

    public static BookingPassenger ToEntity(
        Guid bookingId,
        CustomBookingPassengerRequest request,
        DateOnly today)
    {
        if (!TryParseDateOfBirth(request.DateOfBirth, out var dateOfBirth))
        {
            throw new ValidationException([new ValidationFailure(nameof(request.DateOfBirth),
                "Ngày sinh không hợp lệ. Dùng định dạng yyyy-MM-dd hoặc dd/MM/yyyy.")]);
        }

        return new BookingPassenger
        {
            BookingId = bookingId,
            FullName = request.FullName.Trim(),
            DateOfBirth = dateOfBirth,
            PassengerType = ResolvePassengerType(dateOfBirth, today)
        };
    }

    public static CustomBookingPassengerDto ToDto(BookingPassenger passenger) =>
        new(
            passenger.Id,
            passenger.FullName,
            passenger.DateOfBirth,
            passenger.PassengerType);

    public static int CountAdults(IEnumerable<BookingPassenger> passengers) =>
        passengers.Count(x => string.Equals(
            x.PassengerType,
            CustomBookingPassengerType.Adult.ToString(),
            StringComparison.OrdinalIgnoreCase));

    public static int CountChildren(IEnumerable<BookingPassenger> passengers) =>
        passengers.Count(x => string.Equals(
            x.PassengerType,
            CustomBookingPassengerType.Child.ToString(),
            StringComparison.OrdinalIgnoreCase));

    public static void EnsurePassengerTypeCountsMatchRequest(
        CustomBooking booking,
        IReadOnlyCollection<BookingPassenger> passengers,
        string propertyName)
    {
        if (booking.AdultCount + booking.ChildCount <= 0)
        {
            return;
        }

        var adultCount = CountAdults(passengers);
        var childCount = CountChildren(passengers);

        if (adultCount <= booking.AdultCount && childCount <= booking.ChildCount)
        {
            return;
        }

        throw new ValidationException([new ValidationFailure(propertyName,
            $"Danh sách hành khách không được vượt quá yêu cầu: tối đa {booking.AdultCount} người lớn và {booking.ChildCount} trẻ em.")]);
    }

    public static string ResolvePassengerType(DateOnly dateOfBirth, DateOnly today) =>
        IsAdult(dateOfBirth, today)
            ? CustomBookingPassengerType.Adult.ToString()
            : CustomBookingPassengerType.Child.ToString();

    public static bool TryParseDateOfBirth(string? value, out DateOnly dateOfBirth)
    {
        dateOfBirth = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = value.Trim();
        if (double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out var serialDate)
            && serialDate is > 0 and < 100000)
        {
            dateOfBirth = DateOnly.FromDateTime(DateTime.FromOADate(serialDate));
            return true;
        }

        if (DateOnly.TryParseExact(
                normalized,
                DateFormats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out dateOfBirth))
        {
            return true;
        }

        return DateOnly.TryParse(normalized, CultureInfo.GetCultureInfo("vi-VN"), out dateOfBirth)
            || DateOnly.TryParse(normalized, CultureInfo.InvariantCulture, out dateOfBirth);
    }

    public static bool IsAdult(DateOnly dateOfBirth, DateOnly today) =>
        CalculateAge(dateOfBirth, today) >= AdultMinimumAge;

    private static int CalculateAge(DateOnly dateOfBirth, DateOnly today)
    {
        var age = today.Year - dateOfBirth.Year;
        if (dateOfBirth > today.AddYears(-age))
        {
            age--;
        }

        return age;
    }
}
