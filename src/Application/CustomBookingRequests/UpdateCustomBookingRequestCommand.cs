using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Exceptions;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;

namespace SaigonWaterbus.Application.CustomBookingRequests;

public sealed record UpdateCustomBookingRequestCommand(
    Guid Id,
    Guid? ServiceId,
    int RequestedNumberOfDecks,
    SeatSetupType RequestedSeatSetupType,
    VesselRentalUnit RentalUnit,
    DateOnly DepartureDate,
    TimeOnly? PreferredStartTime,
    Guid FromStationId,
    Guid ToStationId,
    int AdultCount,
    int ChildCount,
    string? SpecialRequests = null,
    IReadOnlyCollection<CreateCustomBookingItineraryStopRequest>? ItineraryStops = null)
    : IRequest<CustomBookingRequestDto>, ICustomBookingTripRequest;

public sealed class UpdateCustomBookingRequestCommandValidator
    : AbstractValidator<UpdateCustomBookingRequestCommand>
{
    public UpdateCustomBookingRequestCommandValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty()
            .WithMessage("Id yêu cầu thuê tàu không hợp lệ.");
        RuleFor(x => x.ServiceId)
            .NotEqual(Guid.Empty)
            .WithMessage("ServiceId không hợp lệ.")
            .When(x => x.ServiceId.HasValue);
        RuleFor(x => x.RentalUnit)
            .IsInEnum()
            .WithMessage("Đơn vị thuê tàu chỉ được là Hour hoặc Day.");
        Include(new CustomBookingTripRequestValidator<UpdateCustomBookingRequestCommand>());
    }
}

public sealed class UpdateCustomBookingRequestCommandHandler
    : IRequestHandler<UpdateCustomBookingRequestCommand, CustomBookingRequestDto>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;
    private readonly TimeProvider _timeProvider;

    public UpdateCustomBookingRequestCommandHandler(
        IApplicationDbContext context,
        IUserContext userContext,
        TimeProvider timeProvider)
    {
        _context = context;
        _userContext = userContext;
        _timeProvider = timeProvider;
    }

    public async Task<CustomBookingRequestDto> Handle(
        UpdateCustomBookingRequestCommand request,
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
        CustomBookingRequestSupport.EnsureDepartureIsInFuture(
            request.DepartureDate,
            request.PreferredStartTime,
            _timeProvider.GetUtcNow());
        var service = await CustomBookingRequestSupport.ResolveVesselRentalServiceAsync(
            _context,
            request.ServiceId,
            cancellationToken);

        var tripPlan = await CustomBookingTripPlanner.BuildAsync(_context, request, cancellationToken);
        _context.Set<CustomBookingItineraryStop>().RemoveRange(customRequest.ItineraryStops);

        customRequest.WaterbusServiceId = service.Id;
        customRequest.WaterbusService = service;
        customRequest.RequestedNumberOfDecks = request.RequestedNumberOfDecks;
        customRequest.RequestedSeatSetupType = request.RequestedSeatSetupType;
        customRequest.RentalUnit = request.RentalUnit;
        customRequest.PreferredVesselId = null;
        customRequest.PreferredVessel = null;
        customRequest.DepartureDate = request.DepartureDate;
        customRequest.PreferredStartTime = request.PreferredStartTime;
        customRequest.PreferredEndTime = tripPlan.RouteEstimate.EstimatedEndTime;
        customRequest.EstimatedEndDate = tripPlan.RouteEstimate.EstimatedEndDate;
        customRequest.EstimatedTravelMinutes = tripPlan.RouteEstimate.EstimatedTravelMinutes;
        customRequest.EstimatedStayMinutes = tripPlan.RouteEstimate.EstimatedStayMinutes;
        customRequest.BufferMinutes = tripPlan.RouteEstimate.BufferMinutes;
        customRequest.EstimatedDurationMinutes = tripPlan.RouteEstimate.EstimatedDurationMinutes;
        customRequest.FromLocation = tripPlan.FromStation.StationName;
        customRequest.ToLocation = tripPlan.ToStation.StationName;
        customRequest.FromStationId = tripPlan.FromStation.Id;
        customRequest.FromStationCode = tripPlan.FromStation.StationCode;
        customRequest.ToStationId = tripPlan.ToStation.Id;
        customRequest.ToStationCode = tripPlan.ToStation.StationCode;
        customRequest.PassengerCount = request.AdultCount + request.ChildCount;
        customRequest.AdultCount = request.AdultCount;
        customRequest.ChildCount = request.ChildCount;
        customRequest.SpecialRequests = string.IsNullOrWhiteSpace(request.SpecialRequests)
            ? null
            : request.SpecialRequests.Trim();
        customRequest.ItineraryStops = tripPlan.ItineraryStops.ToList();

        await _context.SaveChangesAsync(cancellationToken);

        customRequest = await CustomBookingRequestSupport.IncludeDetails(_context.Set<CustomBookingRequest>())
            .SingleAsync(x => x.Id == request.Id, cancellationToken);

        return CustomBookingRequestDto.From(customRequest, tripPlan.RouteSegments);
    }
}
