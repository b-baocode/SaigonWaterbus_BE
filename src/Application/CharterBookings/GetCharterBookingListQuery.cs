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

        var bookings = await CharterBookingQuerySupport.BuildBaseQuery(_context)
            .Include(b => b.Boat)
            .Include(b => b.FromStation)
            .Include(b => b.ToStation)
            .Where(b => b.UserId == userId)
            .OrderByDescending(b => b.Created)
            .ToListAsync(cancellationToken);

        return bookings.Select(CharterBookingListItemMapper.ToDto).ToList();
    }
}

internal static class CharterBookingListItemMapper
{
    public static CharterBookingListItemDto ToDto(Booking booking) =>
        new(
            booking.Id,
            booking.BookingCode,
            booking.BookingStatus.ToString(),
            booking.PaymentStatus,
            booking.DepartureDate.GetValueOrDefault().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            booking.StartTime?.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
            booking.RentalUnit.GetValueOrDefault().ToString(),
            booking.DurationValue.GetValueOrDefault(),
            booking.AdultCount.GetValueOrDefault(),
            booking.ChildCount.GetValueOrDefault(),
            booking.PassengerCount.GetValueOrDefault(),
            booking.FromStation?.StationName,
            booking.ToStation?.StationName,
            booking.Boat?.Name,
            booking.BookingStatus == BookingStatus.PendingQuote ? null : booking.SubtotalAmount,
            booking.BookingStatus == BookingStatus.PendingQuote ? null : booking.TotalAmount,
            ToRequestedBoatDtos(booking.RequestedBoatDecks, booking.RequestedBoatTypes, booking.PreferredSeatSetupType),
            booking.HoldExpiresAt);

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
