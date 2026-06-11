using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;

namespace SaigonWaterbus.Application.CustomBookingRequests;

public sealed record GetCustomBookingRequestDetailQuery(Guid Id) : IRequest<CustomBookingRequestDto>;

public sealed class GetCustomBookingRequestDetailQueryHandler
    : IRequestHandler<GetCustomBookingRequestDetailQuery, CustomBookingRequestDto>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;

    public GetCustomBookingRequestDetailQueryHandler(IApplicationDbContext context, IUserContext userContext)
    {
        _context = context;
        _userContext = userContext;
    }

    public async Task<CustomBookingRequestDto> Handle(
        GetCustomBookingRequestDetailQuery request,
        CancellationToken cancellationToken)
    {
        var actor = await AuthSupport.GetCurrentUserWithRoleAsync(_context, _userContext, cancellationToken);
        var query = CustomBookingRequestSupport.ApplyVisibility(
            CustomBookingRequestSupport.IncludeDetails(_context.Set<CustomBookingRequest>()),
            actor);

        var customRequest = await query.SingleOrDefaultAsync(x => x.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy yêu cầu thuê tàu.");

        return CustomBookingRequestDto.From(customRequest);
    }
}
