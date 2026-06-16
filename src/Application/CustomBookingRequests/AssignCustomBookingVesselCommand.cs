using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;

namespace SaigonWaterbus.Application.CustomBookingRequests;

public sealed record AssignCustomBookingVesselCommand(
    Guid Id,
    Guid VesselId) : IRequest<CustomBookingRequestDto>;

public sealed class AssignCustomBookingVesselCommandValidator
    : AbstractValidator<AssignCustomBookingVesselCommand>
{
    public AssignCustomBookingVesselCommandValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty()
            .WithMessage("Id yêu cầu thuê tàu không hợp lệ.");
        RuleFor(x => x.VesselId)
            .NotEmpty()
            .WithMessage("VesselId là bắt buộc.");
    }
}

public sealed class AssignCustomBookingVesselCommandHandler
    : IRequestHandler<AssignCustomBookingVesselCommand, CustomBookingRequestDto>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;
    private readonly TimeProvider _timeProvider;

    public AssignCustomBookingVesselCommandHandler(
        IApplicationDbContext context,
        IUserContext userContext,
        TimeProvider timeProvider)
    {
        _context = context;
        _userContext = userContext;
        _timeProvider = timeProvider;
    }

    public async Task<CustomBookingRequestDto> Handle(
        AssignCustomBookingVesselCommand request,
        CancellationToken cancellationToken)
    {
        var actor = await CustomBookingRequestSupport.EnsureCurrentUserCanManageCustomBookingRequestsAsync(
            _context,
            _userContext,
            cancellationToken);
        var customRequest = await CustomBookingRequestSupport.IncludeDetails(_context.Set<CustomBookingRequest>())
            .SingleOrDefaultAsync(x => x.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy yêu cầu thuê tàu.");

        CustomBookingRequestSupport.EnsureCanAssignVessel(customRequest);

        var vessel = await _context.Set<Vessel>()
            .Include(x => x.RentalPrices)
            .SingleOrDefaultAsync(x => x.Id == request.VesselId, cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy tàu.");

        CustomBookingRequestSupport.EnsureVesselMatchesRequest(customRequest, vessel);

        customRequest.AssignedVesselId = vessel.Id;
        customRequest.AssignedVessel = vessel;
        customRequest.AssignedAt = _timeProvider.GetUtcNow();
        customRequest.AssignedByUserId = actor.Id;
        customRequest.StatusReason = null;

        await _context.SaveChangesAsync(cancellationToken);

        var routeSegments = await CustomBookingRequestSupport.GetMatchingRouteSegmentsAsync(
            _context,
            customRequest,
            cancellationToken);

        return CustomBookingRequestDto.From(customRequest, routeSegments);
    }
}
