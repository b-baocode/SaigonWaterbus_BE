using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
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

        return await CharterBookingQuerySupport.BuildBaseQuery(_context)
            .Where(b => b.UserId == userId)
            .OrderByDescending(b => b.Created)
            .Select(b => new CharterBookingListItemDto(
                b.Id,
                b.BookingCode,
                b.Created,
                b.DepartureDate.GetValueOrDefault(),
                b.BookingStatus.ToString(),
                b.TotalAmount,
                b.Boat != null ? b.Boat.Name : null))
            .ToListAsync(cancellationToken);
    }
}
