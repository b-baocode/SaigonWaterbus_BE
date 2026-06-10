using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.Bookings;

public sealed record GetBookingListQuery : IRequest<IReadOnlyList<BookingListItemDto>>;

public sealed class GetBookingListQueryHandler : IRequestHandler<GetBookingListQuery, IReadOnlyList<BookingListItemDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;

    public GetBookingListQueryHandler(IApplicationDbContext context, IUserContext userContext)
    {
        _context = context;
        _userContext = userContext;
    }

    public async Task<IReadOnlyList<BookingListItemDto>> Handle(
        GetBookingListQuery request, CancellationToken cancellationToken)
    {
        var userId = _userContext.UserId
            ?? throw new ValidationException([]);

        return await _context.Set<Booking>()
            .Where(b => b.UserId == userId)
            .OrderByDescending(b => b.BookedAt)
            .Select(b => new BookingListItemDto(
                b.Id,
                b.BookingCode,
                b.BookedAt,
                b.BookingStatus.ToString(),
                b.TotalAmount,
                b.Items.Count(i => i.ItemStatus != BookingItemStatus.Cancelled)))
            .ToListAsync(cancellationToken);
    }
}
