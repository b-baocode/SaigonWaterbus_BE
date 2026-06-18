using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using ForbiddenAccessException = SaigonWaterbus.Application.Common.Exceptions.ForbiddenAccessException;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;

namespace SaigonWaterbus.Application.CustomBookingRequests;

public sealed record AcceptCustomBookingQuoteCommand(Guid Id) : IRequest<CustomBookingRequestDto>;

public sealed class AcceptCustomBookingQuoteCommandHandler
    : IRequestHandler<AcceptCustomBookingQuoteCommand, CustomBookingRequestDto>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;
    private readonly TimeProvider _timeProvider;

    public AcceptCustomBookingQuoteCommandHandler(
        IApplicationDbContext context,
        IUserContext userContext,
        TimeProvider timeProvider)
    {
        _context = context;
        _userContext = userContext;
        _timeProvider = timeProvider;
    }

    public async Task<CustomBookingRequestDto> Handle(
        AcceptCustomBookingQuoteCommand request,
        CancellationToken cancellationToken)
    {
        var actor = await AuthSupport.GetCurrentUserWithRoleAsync(_context, _userContext, cancellationToken);
        var customRequest = await CustomBookingRequestSupport.IncludeDetails(_context.Set<CustomBookingRequest>())
            .SingleOrDefaultAsync(x => x.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy yêu cầu thuê tàu.");

        if (customRequest.UserId != actor.Id)
        {
            throw new ForbiddenAccessException();
        }

        var now = _timeProvider.GetUtcNow();
        CustomBookingRequestSupport.EnsureCanAcceptQuote(customRequest, now);
        CustomBookingRequestSupport.EnsureVesselMatchesRequest(customRequest, customRequest.AssignedVessel!);
        var routeSegments = await CustomBookingRequestSupport.GetMatchingRouteSegmentsAsync(
            _context,
            customRequest,
            cancellationToken);
        CustomBookingRequestSupport.ApplyRouteEstimate(customRequest, routeSegments);
        await CustomBookingAvailability.EnsureVesselAvailableAsync(
            _context,
            customRequest,
            customRequest.AssignedVesselId!.Value,
            cancellationToken);

        customRequest.Status = CustomBookingRequestStatus.Confirmed;
        customRequest.StatusReason = null;
        customRequest.QuoteAcceptedAt = now;

        await _context.SaveChangesAsync(cancellationToken);

        return CustomBookingRequestDto.From(customRequest, routeSegments);
    }
}
