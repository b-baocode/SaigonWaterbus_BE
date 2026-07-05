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
            .Where(b => b.UserId == userId)
            .OrderByDescending(b => b.Created)
            .Select(b => new
            {
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
                FromStationName = b.FromStation != null ? b.FromStation.StationName : null,
                ToStationName = b.ToStation != null ? b.ToStation.StationName : null,
                BoatName = b.Boat != null ? b.Boat.Name : null,
                b.SubtotalAmount,
                b.TotalAmount,
                b.RequestedBoatTypes,
                b.PreferredSeatSetupType
            })
            .ToListAsync(cancellationToken);

        return bookings
            .Select(b => new CharterBookingListItemDto(
                b.Id,
                b.BookingCode,
                b.BookingStatus.ToString(),
                b.PaymentStatus,
                b.DepartureDate.GetValueOrDefault().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                b.StartTime?.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
                b.RentalUnit.GetValueOrDefault().ToString(),
                b.DurationValue.GetValueOrDefault(),
                b.AdultCount.GetValueOrDefault(),
                b.ChildCount.GetValueOrDefault(),
                b.PassengerCount.GetValueOrDefault(),
                b.FromStationName,
                b.ToStationName,
                b.BoatName,
                b.BookingStatus == BookingStatus.PendingQuote ? null : b.SubtotalAmount,
                b.BookingStatus == BookingStatus.PendingQuote ? null : b.TotalAmount,
                ToRequestedBoatDtos(b.RequestedBoatTypes, b.PreferredSeatSetupType)))
            .ToList();
    }

    private static IReadOnlyList<CharterBookingListRequestedBoatDto> ToRequestedBoatDtos(
        string? requestedBoatTypes,
        SeatSetupType? preferredSeatSetupType)
    {
        var requestedBoats = CharterBookingBoatSelectionSupport.ToDtos(requestedBoatTypes)
            .Select(x => new CharterBookingListRequestedBoatDto(x.SeatSetupType))
            .ToArray();

        if (requestedBoats.Length > 0)
        {
            return requestedBoats;
        }

        return preferredSeatSetupType.HasValue
            ? [new CharterBookingListRequestedBoatDto(preferredSeatSetupType.Value.ToString())]
            : [];
    }
}
