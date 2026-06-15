using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Application.CustomBookingRequests;

public sealed record GetCustomBookingRequestsQuery(
    CustomBookingRequestStatus? Status = null,
    DateOnly? DepartureDate = null) : IRequest<IReadOnlyList<CustomBookingRequestDto>>;

public sealed class GetCustomBookingRequestsQueryHandler
    : IRequestHandler<GetCustomBookingRequestsQuery, IReadOnlyList<CustomBookingRequestDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;

    public GetCustomBookingRequestsQueryHandler(IApplicationDbContext context, IUserContext userContext)
    {
        _context = context;
        _userContext = userContext;
    }

    public async Task<IReadOnlyList<CustomBookingRequestDto>> Handle(
        GetCustomBookingRequestsQuery request,
        CancellationToken cancellationToken)
    {
        var actor = await AuthSupport.GetCurrentUserWithRoleAsync(_context, _userContext, cancellationToken);
        var query = CustomBookingRequestSupport.ApplyVisibility(
            CustomBookingRequestSupport.IncludeDetails(_context.Set<CustomBookingRequest>()),
            actor);

        if (request.Status.HasValue)
        {
            query = query.Where(x => x.Status == request.Status.Value);
        }

        if (request.DepartureDate.HasValue)
        {
            query = query.Where(x => x.DepartureDate == request.DepartureDate.Value);
        }

        var requests = await query
            .OrderByDescending(x => x.Created)
            .ToListAsync(cancellationToken);

        var result = new List<CustomBookingRequestDto>(requests.Count);
        foreach (var customRequest in requests)
        {
            var routeSegments = await CustomBookingRequestSupport.GetMatchingRouteSegmentsAsync(
                _context,
                customRequest,
                cancellationToken);
            result.Add(CustomBookingRequestDto.From(customRequest, routeSegments));
        }

        return result;
    }
}
