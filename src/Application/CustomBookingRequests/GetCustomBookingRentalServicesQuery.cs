using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Exceptions;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Application.CustomBookingRequests;

public sealed record GetCustomBookingRentalServicesQuery()
    : IRequest<IReadOnlyCollection<CustomBookingServiceDto>>;

public sealed class GetCustomBookingRentalServicesQueryHandler
    : IRequestHandler<GetCustomBookingRentalServicesQuery, IReadOnlyCollection<CustomBookingServiceDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;

    public GetCustomBookingRentalServicesQueryHandler(
        IApplicationDbContext context,
        IUserContext userContext)
    {
        _context = context;
        _userContext = userContext;
    }

    public async Task<IReadOnlyCollection<CustomBookingServiceDto>> Handle(
        GetCustomBookingRentalServicesQuery request,
        CancellationToken cancellationToken)
    {
        var actor = await AuthSupport.GetCurrentUserWithRoleAsync(_context, _userContext, cancellationToken);
        if (!AuthSupport.IsCustomer(actor)
            && !AuthSupport.IsAdmin(actor)
            && !AuthSupport.IsManager(actor)
            && !AuthSupport.IsStaff(actor))
        {
            throw new ForbiddenAccessException();
        }

        return await _context.WaterbusServices
            .AsNoTracking()
            .Where(x => x.IsActive && x.BookingMode == BookingMode.VesselRental)
            .OrderBy(x => x.DisplayOrder)
            .ThenBy(x => x.Code)
            .Select(x => new CustomBookingServiceDto(
                x.Id,
                x.Code,
                x.Name,
                x.BookingMode))
            .ToArrayAsync(cancellationToken);
    }
}
