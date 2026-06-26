using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.CustomBookings;

public sealed record GetCustomBookingListQuery : IRequest<IReadOnlyList<CustomBookingListItemDto>>;

public sealed class GetCustomBookingListQueryHandler
    : IRequestHandler<GetCustomBookingListQuery, IReadOnlyList<CustomBookingListItemDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;

    public GetCustomBookingListQueryHandler(IApplicationDbContext context, IUserContext userContext)
    {
        _context = context;
        _userContext = userContext;
    }

    public async Task<IReadOnlyList<CustomBookingListItemDto>> Handle(
        GetCustomBookingListQuery request, CancellationToken cancellationToken)
    {
        var userId = _userContext.UserId
            ?? throw new ValidationException([]);

        return await _context.Set<CustomBooking>()
            .Where(b => b.UserId == userId)
            .OrderByDescending(b => b.Created)
            .Select(b => new CustomBookingListItemDto(
                b.Id,
                b.BookingCode,
                b.Created,
                b.DepartureDate,
                b.BookingStatus.ToString(),
                b.TotalAmount,
                b.Boat != null ? b.Boat.Name : null))
            .ToListAsync(cancellationToken);
    }
}
