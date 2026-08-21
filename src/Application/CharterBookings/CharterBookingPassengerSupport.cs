using System.Globalization;
using FluentValidation.Results;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.CharterBookings;

internal static class CharterBookingPassengerSupport
{
    public const int AdultMinimumAge = 12;
    public const string ApprovalStatusApproved = "Approved";
    public const string ApprovalStatusPending = "Pending";
    public const string ApprovalStatusRejected = "Rejected";
    public const int MaxPassengerAddRequestCount = 1;
    public static readonly TimeSpan ManifestUpdateCutoff = TimeSpan.FromHours(24);
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
                PassengerType = ResolvePassengerType(birthYear, today),
                ApprovalStatus = ApprovalStatusApproved
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
                    PassengerType = inferredPassengerType,
                    ApprovalStatus = ApprovalStatusApproved
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
            PassengerType = ResolvePassengerType(dateOfBirth, today),
            ApprovalStatus = ApprovalStatusApproved
        };
    }

    public static CharterBookingPassengerDto ToDto(BookingPassenger passenger) =>
        new(
            passenger.Id,
            passenger.FullName,
            passenger.DateOfBirth,
            passenger.BirthYear,
            passenger.PassengerType,
            NormalizeApprovalStatus(passenger.ApprovalStatus),
            passenger.RequestBatchId,
            passenger.RequestedAt,
            passenger.ReviewedAt,
            passenger.ReviewNote);

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

    public static string GetPassengerTypeName(string? passengerType) =>
        string.Equals(passengerType, CharterBookingPassengerType.Child.ToString(), StringComparison.OrdinalIgnoreCase)
            ? "Trẻ em"
            : "Người lớn";

    public static void EnsurePassengerCountDoesNotExceedSelectedBoatCapacity(
        Booking booking,
        int passengerCount,
        string propertyName)
    {
        var capacity = ResolveSelectedBoatCapacity(booking);
        if (passengerCount <= capacity)
        {
            return;
        }

        throw new ValidationException([new ValidationFailure(propertyName,
            $"Danh sách hành khách không được vượt quá sức chứa của tàu đã chọn ({capacity}).")]);
    }

    public static void EnsurePassengerAddRequestCountAvailable(Booking booking, string propertyName)
    {
        var usedRequestCount = booking.Passengers
            .Where(x => x.RequestBatchId.HasValue)
            .Select(x => x.RequestBatchId!.Value)
            .Distinct()
            .Count();
        if (usedRequestCount < MaxPassengerAddRequestCount)
        {
            return;
        }

        throw new ValidationException([new ValidationFailure(propertyName,
            $"Mỗi charter booking chỉ được gửi yêu cầu thêm hành khách tối đa {MaxPassengerAddRequestCount} lần.")]);
    }

    public static void EnsureManifestCanBeUpdatedBeforeCutoff(
        Booking booking,
        DateTimeOffset now,
        string propertyName)
    {
        if (!booking.DepartureDate.HasValue)
        {
            throw new ValidationException([new ValidationFailure(propertyName,
                "Không xác định được ngày khởi hành của charter booking.")]);
        }

        var departureTimeUtc = CharterBookingTripSupport.ResolveDepartureTimeUtc(booking);
        if (now < departureTimeUtc.Subtract(ManifestUpdateCutoff))
        {
            return;
        }

        throw new ValidationException([new ValidationFailure(propertyName,
            "Không thể cập nhật danh sách hành khách trong vòng 24 giờ trước giờ khởi hành.")]);
    }

    public static bool IsApproved(BookingPassenger passenger) =>
        string.Equals(
            NormalizeApprovalStatus(passenger.ApprovalStatus),
            ApprovalStatusApproved,
            StringComparison.OrdinalIgnoreCase);

    public static bool IsPending(BookingPassenger passenger) =>
        string.Equals(
            NormalizeApprovalStatus(passenger.ApprovalStatus),
            ApprovalStatusPending,
            StringComparison.OrdinalIgnoreCase);

    public static bool IsRejected(BookingPassenger passenger) =>
        string.Equals(
            NormalizeApprovalStatus(passenger.ApprovalStatus),
            ApprovalStatusRejected,
            StringComparison.OrdinalIgnoreCase);

    public static string NormalizeApprovalStatus(string? status) =>
        string.IsNullOrWhiteSpace(status) ? ApprovalStatusApproved : status.Trim();

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

    private static int ResolveSelectedBoatCapacity(Booking booking)
    {
        var selectedBoatCapacity = booking.CharterBoats
            .Where(x => x.Boat is not null)
            .GroupBy(x => x.BoatId)
            .Sum(x => x.First().Boat.SeatCount);
        if (selectedBoatCapacity > 0)
        {
            return selectedBoatCapacity;
        }

        if (booking.Boat is not null && booking.Boat.SeatCount > 0)
        {
            return booking.Boat.SeatCount;
        }

        return booking.PassengerCount.GetValueOrDefault();
    }

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
