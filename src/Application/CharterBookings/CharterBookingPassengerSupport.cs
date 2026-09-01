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
    public static readonly TimeSpan ManifestUpdateCutoff = TimeSpan.FromHours(48);
    private const int MinimumBirthYear = 1900;

    public static BookingPassenger ToEntity(
        Guid bookingId,
        CharterBookingPassengerRequest request,
        DateOnly today,
        string? inferredPassengerType = null,
        string birthYearPropertyName = nameof(CharterBookingPassengerRequest.BirthYear),
        string fullNamePropertyName = nameof(CharterBookingPassengerRequest.FullName))
    {
        if (string.IsNullOrWhiteSpace(request.FullName))
        {
            throw new ValidationException([new ValidationFailure(fullNamePropertyName,
                "fullName is required.")]);
        }

        if (request.BirthYear.HasValue)
        {
            var birthYear = request.BirthYear.Value;
            if (!IsValidBirthYear(birthYear, today))
            {
                throw new ValidationException([new ValidationFailure(birthYearPropertyName,
                    birthYear > today.Year ? "Năm sinh không được ở tương lai." : "Năm sinh không hợp lệ.")]);
            }

            return new BookingPassenger
            {
                BookingId = bookingId,
                FullName = request.FullName.Trim(),
                BirthYear = birthYear,
                PassengerType = ResolvePassengerType(birthYear, today),
                ApprovalStatus = ApprovalStatusApproved
            };
        }

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

        throw new ValidationException([new ValidationFailure(birthYearPropertyName,
            "birthYear is required.")]);
    }

    public static CharterBookingPassengerDto ToDto(BookingPassenger passenger) =>
        new(
            passenger.Id,
            passenger.FullName,
            passenger.BirthYear,
            passenger.PassengerType,
            NormalizeApprovalStatus(passenger.ApprovalStatus),
            passenger.RequestBatchId,
            passenger.RequestedAt,
            passenger.ReviewedAt,
            passenger.ReviewNote,
            passenger.TripId,
            passenger.TripSeatId,
            passenger.TripSeat?.Seat?.Code ?? passenger.CharterSeat?.Code);

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
            "Không thể cập nhật danh sách hành khách trong vòng 48 giờ trước giờ khởi hành.")]);
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

    public static string ResolvePassengerType(int birthYear, DateOnly today) =>
        today.Year - birthYear >= AdultMinimumAge
            ? CharterBookingPassengerType.Adult.ToString()
            : CharterBookingPassengerType.Child.ToString();

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
}
