using System.Globalization;
using FluentValidation.Results;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.CharterBookings;

internal static class CharterBookingPassengerSupport
{
    public const int AdultMinimumAge = 12;
    private const int MinimumBirthYear = 1900;
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
        CharterBookingPassengerRequest request,
        DateOnly today,
        string? inferredPassengerType = null,
        string dateOfBirthPropertyName = nameof(CharterBookingPassengerRequest.DateOfBirth),
        string fullNamePropertyName = nameof(CharterBookingPassengerRequest.FullName))
    {
        if (string.IsNullOrWhiteSpace(request.FullName))
        {
            throw new ValidationException([new ValidationFailure(fullNamePropertyName,
                "fullName is required.")]);
        }

        if (TryResolveBirthYear(request, today, out var birthYear))
        {
            return new BookingPassenger
            {
                BookingId = bookingId,
                FullName = request.FullName.Trim(),
                BirthYear = birthYear,
                PassengerType = ResolvePassengerType(birthYear, today)
            };
        }

        if (!TryParseDateOfBirth(request.DateOfBirth, out var dateOfBirth))
        {
            if (!string.IsNullOrWhiteSpace(inferredPassengerType))
            {
                return new BookingPassenger
                {
                    BookingId = bookingId,
                    FullName = request.FullName.Trim(),
                    PassengerType = inferredPassengerType
                };
            }

            var message = string.IsNullOrWhiteSpace(request.DateOfBirth)
                ? "birthYear is required."
                : "Năm sinh/ngày sinh không hợp lệ. Dùng năm yyyy hoặc ngày yyyy-MM-dd/dd/MM/yyyy.";
            throw new ValidationException([new ValidationFailure(dateOfBirthPropertyName, message)]);
        }

        if (dateOfBirth > today)
        {
            throw new ValidationException([new ValidationFailure(dateOfBirthPropertyName,
                "Ngày sinh không được ở tương lai.")]);
        }

        return new BookingPassenger
        {
            BookingId = bookingId,
            FullName = request.FullName.Trim(),
            DateOfBirth = dateOfBirth,
            BirthYear = dateOfBirth.Year,
            PassengerType = ResolvePassengerType(dateOfBirth, today)
        };
    }

    public static CharterBookingPassengerDto ToDto(BookingPassenger passenger) =>
        new(
            passenger.Id,
            passenger.FullName,
            passenger.DateOfBirth,
            passenger.BirthYear,
            passenger.PassengerType);

    public static int CountAdults(IEnumerable<BookingPassenger> passengers) =>
        passengers.Count(x => string.Equals(
            x.PassengerType,
            CharterBookingPassengerType.Adult.ToString(),
            StringComparison.OrdinalIgnoreCase));

    public static int CountChildren(IEnumerable<BookingPassenger> passengers) =>
        passengers.Count(x => string.Equals(
            x.PassengerType,
            CharterBookingPassengerType.Child.ToString(),
            StringComparison.OrdinalIgnoreCase));

    public static void EnsurePassengerTypeCountsMatchRequest(
        Booking booking,
        IReadOnlyCollection<BookingPassenger> passengers,
        string propertyName)
    {
        if (booking.AdultCount.GetValueOrDefault() + booking.ChildCount.GetValueOrDefault() <= 0)
        {
            return;
        }

        var adultCount = CountAdults(passengers);
        var childCount = CountChildren(passengers);

        if (adultCount <= booking.AdultCount.GetValueOrDefault() && childCount <= booking.ChildCount.GetValueOrDefault())
        {
            return;
        }

        throw new ValidationException([new ValidationFailure(propertyName,
            $"Danh sách hành khách không được vượt quá yêu cầu: tối đa {booking.AdultCount.GetValueOrDefault()} người lớn và {booking.ChildCount.GetValueOrDefault()} trẻ em.")]);
    }

    public static string ResolvePassengerType(DateOnly dateOfBirth, DateOnly today) =>
        IsAdult(dateOfBirth, today)
            ? CharterBookingPassengerType.Adult.ToString()
            : CharterBookingPassengerType.Child.ToString();

    public static string ResolvePassengerType(int birthYear, DateOnly today) =>
        today.Year - birthYear >= AdultMinimumAge
            ? CharterBookingPassengerType.Adult.ToString()
            : CharterBookingPassengerType.Child.ToString();

    public static bool TryParseDateOfBirth(string? value, out DateOnly dateOfBirth)
    {
        dateOfBirth = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = value.Trim();
        if (TryParseBirthYear(normalized, out _))
        {
            return false;
        }

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

    public static bool TryParseBirthYear(string? value, out int birthYear)
    {
        birthYear = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = value.Trim();
        return normalized.Length == 4
            && int.TryParse(normalized, NumberStyles.None, CultureInfo.InvariantCulture, out birthYear)
            && birthYear is >= MinimumBirthYear and <= 9999;
    }

    public static bool IsValidBirthYear(int birthYear, DateOnly today) =>
        birthYear >= MinimumBirthYear && birthYear <= today.Year;

    public static bool IsAdult(DateOnly dateOfBirth, DateOnly today) =>
        CalculateAge(dateOfBirth, today) >= AdultMinimumAge;

    private static bool TryResolveBirthYear(
        CharterBookingPassengerRequest request,
        DateOnly today,
        out int birthYear)
    {
        birthYear = default;
        if (request.BirthYear.HasValue)
        {
            birthYear = request.BirthYear.Value;
            if (!IsValidBirthYear(birthYear, today))
            {
                throw new ValidationException([new ValidationFailure(nameof(request.BirthYear),
                    birthYear > today.Year ? "Năm sinh không được ở tương lai." : "Năm sinh không hợp lệ.")]);
            }

            return true;
        }

        if (!TryParseBirthYear(request.DateOfBirth, out birthYear))
        {
            return false;
        }

        if (!IsValidBirthYear(birthYear, today))
        {
            throw new ValidationException([new ValidationFailure(nameof(request.DateOfBirth),
                birthYear > today.Year ? "Năm sinh không được ở tương lai." : "Năm sinh không hợp lệ.")]);
        }

        return true;
    }

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
