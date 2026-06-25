using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.CustomBookings;

public sealed record GetCustomBookingDetailQuery(Guid BookingId) : IRequest<CustomBookingDetailDto>;

public sealed class GetCustomBookingDetailQueryHandler
    : IRequestHandler<GetCustomBookingDetailQuery, CustomBookingDetailDto>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;

    public GetCustomBookingDetailQueryHandler(IApplicationDbContext context, IUserContext userContext)
    {
        _context = context;
        _userContext = userContext;
    }

    public async Task<CustomBookingDetailDto> Handle(
        GetCustomBookingDetailQuery request, CancellationToken cancellationToken)
    {
        var userId = _userContext.UserId
            ?? throw new ValidationException([]);

        var booking = await CustomBookingQuerySupport.BuildDetailQuery(_context)
            .SingleOrDefaultAsync(b => b.Id == request.BookingId, cancellationToken)
            ?? throw new NotFoundException("Custom booking not found.");

        if (booking.UserId != userId)
            throw new NotFoundException("Custom booking not found.");

        var relatedRoutes = await CustomBookingRoutePricingSupport.LoadRelatedRoutesAsync(
            _context,
            booking,
            cancellationToken);

        return CustomBookingQuerySupport.ToDetailDto(booking, relatedRoutes);
    }
}
