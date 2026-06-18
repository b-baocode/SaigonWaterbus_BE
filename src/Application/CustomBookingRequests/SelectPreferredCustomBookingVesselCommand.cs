using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Exceptions;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;

namespace SaigonWaterbus.Application.CustomBookingRequests;

public sealed record SelectPreferredCustomBookingVesselCommand(
    Guid Id,
    Guid VesselId) : IRequest<CustomBookingRequestDto>;

public sealed class SelectPreferredCustomBookingVesselCommandValidator
    : AbstractValidator<SelectPreferredCustomBookingVesselCommand>
{
    public SelectPreferredCustomBookingVesselCommandValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty()
            .WithMessage("Id yêu cầu thuê tàu không hợp lệ.");
        RuleFor(x => x.VesselId)
            .NotEmpty()
            .WithMessage("VesselId là bắt buộc.");
    }
}

public sealed class SelectPreferredCustomBookingVesselCommandHandler
    : IRequestHandler<SelectPreferredCustomBookingVesselCommand, CustomBookingRequestDto>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;

    public SelectPreferredCustomBookingVesselCommandHandler(
        IApplicationDbContext context,
        IUserContext userContext)
    {
        _context = context;
        _userContext = userContext;
    }

    public async Task<CustomBookingRequestDto> Handle(
        SelectPreferredCustomBookingVesselCommand request,
        CancellationToken cancellationToken)
    {
        var actor = await AuthSupport.GetCurrentUserWithRoleAsync(_context, _userContext, cancellationToken);
        if (!AuthSupport.IsCustomer(actor))
        {
            throw new ForbiddenAccessException();
        }

        var customRequest = await CustomBookingRequestSupport.IncludeDetails(_context.Set<CustomBookingRequest>())
            .SingleOrDefaultAsync(x => x.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy yêu cầu thuê tàu.");

        if (customRequest.UserId != actor.Id)
        {
            throw new ForbiddenAccessException();
        }

        CustomBookingRequestSupport.EnsureCanEdit(customRequest);

        var vessel = await _context.Set<Vessel>()
            .Include(x => x.RentalPrices)
            .SingleOrDefaultAsync(x => x.Id == request.VesselId, cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy tàu.");

        CustomBookingRequestSupport.EnsureVesselMatchesRequest(customRequest, vessel);
        await CustomBookingAvailability.EnsureVesselAvailableAsync(
            _context,
            customRequest,
            vessel.Id,
            cancellationToken);

        customRequest.PreferredVesselId = vessel.Id;
        customRequest.PreferredVessel = vessel;
        customRequest.StatusReason = null;

        await _context.SaveChangesAsync(cancellationToken);

        var routeSegments = await CustomBookingRequestSupport.GetMatchingRouteSegmentsAsync(
            _context,
            customRequest,
            cancellationToken);

        return CustomBookingRequestDto.From(customRequest, routeSegments);
    }
}
