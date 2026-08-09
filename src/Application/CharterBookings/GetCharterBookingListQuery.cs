using System.Globalization;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.CharterBookings;

public sealed record GetCharterBookingListQuery : IRequest<IReadOnlyList<CharterBookingListItemDto>>;

public sealed class GetCharterBookingListQueryHandler
    : IRequestHandler<GetCharterBookingListQuery, IReadOnlyList<CharterBookingListItemDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;

    public GetCharterBookingListQueryHandler(IApplicationDbContext context, IUserContext userContext)
    {
        _context = context;
        _userContext = userContext;
    }

    public async Task<IReadOnlyList<CharterBookingListItemDto>> Handle(
        GetCharterBookingListQuery request, CancellationToken cancellationToken)
    {
        var userId = _userContext.UserId
            ?? throw new ValidationException([]);

        var rows = await CharterBookingQuerySupport.BuildBaseQuery(_context)
            .AsNoTracking()
            .Where(b => b.UserId == userId)
            .OrderByDescending(b => b.Created)
            .Select(b => new CharterBookingListItemRow(
                b.Id,
                b.BookingCode,
                b.BookingStatus,
                b.PaymentStatus,
                b.DepartureDate,
                b.StartTime,
                b.RentalUnit,
                b.DurationValue,
                b.AdultCount,
                b.ChildCount,
                b.PassengerCount,
                b.FromStationId,
                b.ToStationId,
                b.FromStation != null ? b.FromStation.StationName : null,
                b.ToStation != null ? b.ToStation.StationName : null,
                b.BoatId,
                b.Boat != null ? b.Boat.Name : null,
                b.SubtotalAmount,
                b.TotalAmount,
                b.DepositAmount,
                b.RequestedBoatDecks,
                b.RequestedBoatTypes,
                b.PreferredSeatSetupType,
                b.HoldExpiresAt))
            .ToListAsync(cancellationToken);

        return rows.Select(CharterBookingListItemMapper.ToDto).ToList();
    }
}

internal sealed record CharterBookingListItemRow(
    Guid Id,
    string BookingCode,
    BookingStatus BookingStatus,
    string PaymentStatus,
    DateOnly? DepartureDate,
    TimeOnly? StartTime,
    BoatRentalUnit? RentalUnit,
    int? DurationValue,
    int? AdultCount,
    int? ChildCount,
    int? PassengerCount,
    Guid? FromStationId,
    Guid? ToStationId,
    string? FromStationName,
    string? ToStationName,
    Guid? BoatId,
    string? BoatName,
    decimal SubtotalAmount,
    decimal TotalAmount,
    decimal DepositAmount,
    string? RequestedBoatDecks,
    string? RequestedBoatTypes,
    SeatSetupType? PreferredSeatSetupType,
    DateTimeOffset? HoldExpiresAt);

internal static class CharterBookingListItemMapper
{
    public static CharterBookingListItemDto ToDto(Booking booking) =>
        ToDto(new CharterBookingListItemRow(
            booking.Id,
            booking.BookingCode,
            booking.BookingStatus,
            booking.PaymentStatus,
            booking.DepartureDate,
            booking.StartTime,
            booking.RentalUnit,
            booking.DurationValue,
            booking.AdultCount,
            booking.ChildCount,
            booking.PassengerCount,
            booking.FromStationId,
            booking.ToStationId,
            booking.FromStation?.StationName,
            booking.ToStation?.StationName,
            booking.BoatId,
            booking.Boat?.Name,
            booking.SubtotalAmount,
            booking.TotalAmount,
            booking.DepositAmount,
            booking.RequestedBoatDecks,
            booking.RequestedBoatTypes,
            booking.PreferredSeatSetupType,
            booking.HoldExpiresAt));

    public static CharterBookingListItemDto ToDto(CharterBookingListItemRow booking) =>
        new(
            booking.Id,
            booking.BookingCode,
            booking.BookingStatus.ToString(),
            booking.PaymentStatus,
            booking.DepartureDate.GetValueOrDefault().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            booking.StartTime?.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
            booking.RentalUnit?.ToString(),
            booking.DurationValue,
            booking.AdultCount.GetValueOrDefault(),
            booking.ChildCount.GetValueOrDefault(),
            booking.PassengerCount.GetValueOrDefault(),
            booking.FromStationName,
            booking.ToStationName,
            booking.BoatName,
            booking.BookingStatus == BookingStatus.PendingQuote ? null : booking.SubtotalAmount,
            booking.BookingStatus == BookingStatus.PendingQuote ? null : booking.TotalAmount,
            // BE tính sẵn để FE hiển thị nút "Đặt cọc" enabled đúng (đã/s chưa cọc).
            suggestedDepositAmount: booking.DepositAmount > 0
                ? 0m
                : decimal.Round(booking.TotalAmount * CharterBookingPaymentSupport.DefaultDepositPercent / 100m, 0, MidpointRounding.AwayFromZero),
            hasDepositPaid: booking.DepositAmount > 0,
            ToRequestedBoatDtos(booking.RequestedBoatDecks, booking.RequestedBoatTypes, booking.PreferredSeatSetupType),
            booking.HoldExpiresAt,
            booking.FromStationId,
            booking.ToStationId,
            booking.BoatId);

    private static IReadOnlyList<CharterBookingListRequestedBoatDto> ToRequestedBoatDtos(
        string? requestedBoatDecks,
        string? requestedBoatTypes,
        SeatSetupType? preferredSeatSetupType)
    {
        var requestedBoats = CharterBookingBoatSelectionSupport.ToDtos(requestedBoatDecks, requestedBoatTypes)
            .Select(x => new CharterBookingListRequestedBoatDto(x.NumberOfDecks, x.SeatSetupType))
            .ToArray();

        if (requestedBoats.Length > 0)
        {
            return requestedBoats;
        }

        return preferredSeatSetupType.HasValue
            ? [new CharterBookingListRequestedBoatDto(null, preferredSeatSetupType.Value.ToString())]
            : [];
    }
}
