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
    private readonly IDatabaseExceptionClassifier _databaseExceptionClassifier;

    public AssignCustomBookingVesselCommandHandler(
        IApplicationDbContext context,
        IUserContext userContext,
        TimeProvider timeProvider,
        IDatabaseExceptionClassifier? databaseExceptionClassifier = null)
    {
        _context = context;
        _userContext = userContext;
        _timeProvider = timeProvider;
        _databaseExceptionClassifier = databaseExceptionClassifier ?? NoOpDatabaseExceptionClassifier.Instance;
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
        var now = _timeProvider.GetUtcNow();
        if (await CustomBookingVesselReservations.ExpireStaleReservationsAsync(
            _context,
            now,
            cancellationToken) > 0)
        {
            await _context.SaveChangesAsync(cancellationToken);
        }

        await CustomBookingVesselReservations.EnsureVesselAvailableAsync(
            _context,
            customRequest,
            vessel.Id,
            now,
            cancellationToken);

        customRequest.AssignedVesselId = vessel.Id;
        customRequest.AssignedVessel = vessel;
        customRequest.AssignedAt = now;
        customRequest.AssignedByUserId = actor.Id;
        customRequest.StatusReason = null;
        await CustomBookingVesselReservations.HoldForQuoteAsync(
            _context,
            customRequest,
            vessel.Id,
            actor.Id,
            now,
            cancellationToken);

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (_databaseExceptionClassifier.IsExclusionConstraintViolation(ex))
        {
            throw CustomBookingVesselReservations.CreateUnavailableException();
        }

        var routeSegments = await CustomBookingRequestSupport.GetMatchingRouteSegmentsAsync(
            _context,
            customRequest,
            cancellationToken);

        return CustomBookingRequestDto.From(customRequest, routeSegments);
    }
}
