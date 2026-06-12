using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Exceptions;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Application.CustomBookingRequests;

public sealed record CreateCustomBookingItineraryStopRequest(
    int StopOrder,
    Guid StationId,
    int StayDurationMinutes,
    string? Note = null);

public sealed record CreateCustomBookingRequestCommand(
    bool UseAccountContact = true,
    string? ContactName = null,
    string? ContactPhone = null,
    string? ContactEmail = null,
    Guid PreferredVesselId = default,
    DateOnly DepartureDate = default,
    TimeOnly? PreferredStartTime = null,
    Guid FromStationId = default,
    Guid ToStationId = default,
    int AdultCount = 0,
    int ChildCount = 0,
    IReadOnlyCollection<CreateCustomBookingItineraryStopRequest>? ItineraryStops = null) : IRequest<CustomBookingRequestDto>;

public sealed class CreateCustomBookingRequestCommandValidator : AbstractValidator<CreateCustomBookingRequestCommand>
{
    public CreateCustomBookingRequestCommandValidator()
    {
        RuleFor(x => x.DepartureDate)
            .Must(x => x != default)
            .WithMessage("Ngày đi là bắt buộc.");

        RuleFor(x => x.PreferredVesselId)
            .NotEmpty()
            .WithMessage("Tàu muốn thuê là bắt buộc.");

        RuleFor(x => x.PreferredStartTime)
            .NotNull()
            .WithMessage("Giờ bắt đầu là bắt buộc.");

        RuleFor(x => x.FromStationId)
            .NotEmpty()
            .WithMessage("Bến bắt đầu là bắt buộc.");

        RuleFor(x => x.ToStationId)
            .NotEmpty()
            .WithMessage("Bến kết thúc là bắt buộc.");

        RuleFor(x => x.AdultCount)
            .GreaterThanOrEqualTo(1)
            .WithMessage("Số người lớn phải lớn hơn hoặc bằng 1.");

        RuleFor(x => x.ChildCount)
            .GreaterThanOrEqualTo(0)
            .WithMessage("Số trẻ em không được âm.");

        RuleFor(x => x)
            .Must(x => x.AdultCount + x.ChildCount <= 500)
            .WithMessage("Tổng số khách không được vượt quá 500.")
            .OverridePropertyName(nameof(CreateCustomBookingRequestCommand.AdultCount));

        RuleFor(x => x.ItineraryStops)
            .Must(x => x is null || x.Count <= 20)
            .WithMessage("Lịch trình không được vượt quá 20 điểm ghé.")
            .Must(HaveUniqueStopOrders)
            .WithMessage("Thứ tự điểm ghé không được trùng nhau.")
            .Must(HaveSequentialStopOrders)
            .WithMessage("Thứ tự điểm ghé phải bắt đầu từ 1 và tăng liên tục.");

        RuleForEach(x => x.ItineraryStops).ChildRules(stop =>
        {
            stop.RuleFor(x => x.StopOrder).GreaterThan(0);
            stop.RuleFor(x => x.StationId).NotEmpty().WithMessage("Bến/điểm ghé là bắt buộc.");
            stop.RuleFor(x => x.StayDurationMinutes)
                .InclusiveBetween(0, 1440)
                .WithMessage("Thời gian dừng phải từ 0 đến 1440 phút.");
            stop.RuleFor(x => x.Note).MaximumLength(500).When(x => x.Note is not null);
        });

        When(x => !x.UseAccountContact, () =>
        {
            RuleFor(x => x.ContactName).NotEmpty().MaximumLength(150);
            RuleFor(x => x.ContactPhone).NotEmpty().MaximumLength(20);
        });

        RuleFor(x => x.ContactName).MaximumLength(150).When(x => x.ContactName is not null);
        RuleFor(x => x.ContactPhone).MaximumLength(20).When(x => x.ContactPhone is not null);
        RuleFor(x => x.ContactEmail).MaximumLength(255).When(x => x.ContactEmail is not null);
    }

    private static bool HaveUniqueStopOrders(IReadOnlyCollection<CreateCustomBookingItineraryStopRequest>? stops)
    {
        if (stops is null)
        {
            return true;
        }

        return stops.Select(x => x.StopOrder).Distinct().Count() == stops.Count;
    }

    private static bool HaveSequentialStopOrders(IReadOnlyCollection<CreateCustomBookingItineraryStopRequest>? stops)
    {
        if (stops is null || stops.Count == 0)
        {
            return true;
        }

        return stops
            .OrderBy(x => x.StopOrder)
            .Select((stop, index) => stop.StopOrder == index + 1)
            .All(x => x);
    }
}

public sealed class CreateCustomBookingRequestCommandHandler
    : IRequestHandler<CreateCustomBookingRequestCommand, CustomBookingRequestDto>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;
    private readonly TimeProvider _timeProvider;

    public CreateCustomBookingRequestCommandHandler(
        IApplicationDbContext context,
        IUserContext userContext,
        TimeProvider timeProvider)
    {
        _context = context;
        _userContext = userContext;
        _timeProvider = timeProvider;
    }

    public async Task<CustomBookingRequestDto> Handle(
        CreateCustomBookingRequestCommand request,
        CancellationToken cancellationToken)
    {
        var user = await AuthSupport.GetCurrentUserWithRoleAsync(_context, _userContext, cancellationToken);
        if (!AuthSupport.IsCustomer(user))
        {
            throw new ForbiddenAccessException();
        }

        var contactName = ResolveContactName(request, user);
        var contactPhone = ResolveContactPhone(request, user);
        var contactEmail = ResolveContactEmail(request, user);

        var normalizedPhone = CustomBookingRequestSupport.NormalizePhoneOrThrow(contactPhone, nameof(request.ContactPhone));
        CustomBookingRequestSupport.EnsureValidEmailIfProvided(contactEmail, nameof(request.ContactEmail));
        CustomBookingRequestSupport.EnsureDepartureDateIsNotPast(
            request.DepartureDate,
            CustomBookingRequestSupport.GetVietnamToday(_timeProvider));

        var contactUserId = await ResolveContactUserIdAsync(normalizedPhone, contactEmail, cancellationToken);
        var vessel = await ResolvePreferredVesselAsync(request.PreferredVesselId, cancellationToken);
        var fromStation = await ResolveStationAsync(request.FromStationId, nameof(request.FromStationId), cancellationToken);
        var toStation = await ResolveStationAsync(request.ToStationId, nameof(request.ToStationId), cancellationToken);
        var itineraryStops = (request.ItineraryStops ?? Array.Empty<CreateCustomBookingItineraryStopRequest>())
            .OrderBy(x => x.StopOrder)
            .ToArray();
        var itineraryStations = await ResolveItineraryStationsAsync(itineraryStops, cancellationToken);

        if (fromStation.Id == toStation.Id && itineraryStops.Length == 0)
        {
            throw AuthSupport.CreateValidationException(
                nameof(request.ToStationId),
                "Nếu bến bắt đầu và bến kết thúc trùng nhau thì phải có ít nhất một điểm ghé.");
        }

        var passengerCount = request.AdultCount + request.ChildCount;
        if (passengerCount > vessel.PassengerCapacity)
        {
            throw AuthSupport.CreateValidationException(
                nameof(request.AdultCount),
                $"Tổng số khách ({passengerCount}) vượt quá sức chứa tàu ({vessel.PassengerCapacity}).");
        }

        var stayMinutes = itineraryStops.Sum(x => x.StayDurationMinutes);
        var timing = CustomBookingRequestSupport.CalculateTimingEstimate(
            request.DepartureDate,
            request.PreferredStartTime!.Value,
            itineraryStops.Length,
            stayMinutes);

        var customRequest = new CustomBookingRequest
        {
            UserId = user.Id,
            ContactUserId = contactUserId,
            ContactName = contactName.Trim(),
            ContactPhone = normalizedPhone,
            ContactEmail = string.IsNullOrWhiteSpace(contactEmail) ? null : contactEmail.Trim(),
            PreferredVesselId = vessel.Id,
            DepartureDate = request.DepartureDate,
            PreferredStartTime = request.PreferredStartTime,
            PreferredEndTime = timing.EstimatedEndTime,
            EstimatedEndDate = timing.EstimatedEndDate,
            EstimatedTravelMinutes = timing.EstimatedTravelMinutes,
            EstimatedStayMinutes = timing.EstimatedStayMinutes,
            BufferMinutes = timing.BufferMinutes,
            EstimatedDurationMinutes = timing.EstimatedDurationMinutes,
            FromLocation = fromStation.StationName,
            ToLocation = toStation.StationName,
            FromStationId = fromStation.Id,
            FromStationCode = fromStation.StationCode,
            ToStationId = toStation.Id,
            ToStationCode = toStation.StationCode,
            ItineraryNote = null,
            PassengerCount = passengerCount,
            AdultCount = request.AdultCount,
            ChildCount = request.ChildCount,
            SpecialRequests = null,
            Status = CustomBookingRequestStatus.PendingReview,
            ItineraryStops = itineraryStops
                .Select(stop => new CustomBookingItineraryStop
                {
                    StopOrder = stop.StopOrder,
                    StationId = stop.StationId,
                    Station = itineraryStations[stop.StationId],
                    StayDurationMinutes = stop.StayDurationMinutes,
                    Note = string.IsNullOrWhiteSpace(stop.Note) ? null : stop.Note.Trim()
                })
                .ToList()
        };

        _context.Set<CustomBookingRequest>().Add(customRequest);
        await _context.SaveChangesAsync(cancellationToken);

        customRequest = await CustomBookingRequestSupport.IncludeDetails(_context.Set<CustomBookingRequest>())
            .SingleAsync(x => x.Id == customRequest.Id, cancellationToken);

        return CustomBookingRequestDto.From(customRequest);
    }

    private static string ResolveContactName(CreateCustomBookingRequestCommand request, User user)
    {
        if (request.UseAccountContact && !string.IsNullOrWhiteSpace(user.FullName))
        {
            return user.FullName;
        }

        if (!string.IsNullOrWhiteSpace(request.ContactName))
        {
            return request.ContactName;
        }

        throw AuthSupport.CreateValidationException(nameof(request.ContactName), "Tên người liên hệ là bắt buộc.");
    }

    private static string ResolveContactPhone(CreateCustomBookingRequestCommand request, User user)
    {
        if (request.UseAccountContact && !string.IsNullOrWhiteSpace(user.PhoneNumber))
        {
            return user.PhoneNumber;
        }

        if (!string.IsNullOrWhiteSpace(request.ContactPhone))
        {
            return request.ContactPhone;
        }

        throw AuthSupport.CreateValidationException(nameof(request.ContactPhone), "Số điện thoại liên hệ là bắt buộc.");
    }

    private static string? ResolveContactEmail(CreateCustomBookingRequestCommand request, User user)
    {
        if (request.UseAccountContact && !string.IsNullOrWhiteSpace(user.Email))
        {
            return user.Email;
        }

        return string.IsNullOrWhiteSpace(request.ContactEmail) ? null : request.ContactEmail;
    }

    private async Task<Guid?> ResolveContactUserIdAsync(
        string normalizedPhone,
        string? contactEmail,
        CancellationToken cancellationToken)
    {
        var normalizedEmail = string.IsNullOrWhiteSpace(contactEmail)
            ? null
            : contactEmail.Trim().ToUpperInvariant();

        var linkedUser = await _context.Set<User>()
            .Where(x => x.NormalizedPhoneNumber == normalizedPhone
                     || (normalizedEmail != null && x.NormalizedEmail == normalizedEmail))
            .FirstOrDefaultAsync(cancellationToken);

        return linkedUser?.Id;
    }

    private async Task<Vessel> ResolvePreferredVesselAsync(
        Guid vesselId,
        CancellationToken cancellationToken)
    {
        var vessel = await _context.Set<Vessel>()
            .Include(x => x.WaterbusService)
            .Include(x => x.RentalPrices)
            .SingleOrDefaultAsync(x => x.Id == vesselId, cancellationToken);

        if (vessel is null)
        {
            throw AuthSupport.CreateValidationException(nameof(CreateCustomBookingRequestCommand.PreferredVesselId), "Tàu muốn thuê không tồn tại.");
        }

        if (vessel.Status != VesselStatus.Active || !vessel.SeatsConfigured)
        {
            throw AuthSupport.CreateValidationException(nameof(CreateCustomBookingRequestCommand.PreferredVesselId), "Tàu chưa sẵn sàng để cho thuê.");
        }

        if (!vessel.WaterbusService.IsActive || vessel.WaterbusService.BookingMode != BookingMode.VesselRental)
        {
            throw AuthSupport.CreateValidationException(nameof(CreateCustomBookingRequestCommand.PreferredVesselId), "Tàu không thuộc dịch vụ thuê tàu custom.");
        }

        if (!vessel.RentalPrices.Any(x => x.RentalUnit == VesselRentalUnit.Day))
        {
            throw AuthSupport.CreateValidationException(nameof(CreateCustomBookingRequestCommand.PreferredVesselId), "Tàu chưa được cấu hình giá thuê theo ngày.");
        }

        return vessel;
    }

    private async Task<Station> ResolveStationAsync(
        Guid stationId,
        string propertyName,
        CancellationToken cancellationToken)
    {
        var station = await _context.Set<Station>()
            .SingleOrDefaultAsync(x => x.Id == stationId, cancellationToken);

        if (station is null)
        {
            throw AuthSupport.CreateValidationException(propertyName, "Bến không tồn tại.");
        }

        if (station.Status != StationStatus.Active)
        {
            throw AuthSupport.CreateValidationException(propertyName, "Bến không hoạt động.");
        }

        return station;
    }

    private async Task<Dictionary<Guid, Station>> ResolveItineraryStationsAsync(
        IReadOnlyCollection<CreateCustomBookingItineraryStopRequest> stops,
        CancellationToken cancellationToken)
    {
        if (stops.Count == 0)
        {
            return new Dictionary<Guid, Station>();
        }

        var stationIds = stops.Select(x => x.StationId).Distinct().ToArray();
        var stations = await _context.Set<Station>()
            .Where(x => stationIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        foreach (var stationId in stationIds)
        {
            if (!stations.TryGetValue(stationId, out var station))
            {
                throw AuthSupport.CreateValidationException(nameof(CreateCustomBookingItineraryStopRequest.StationId), "Điểm ghé không tồn tại.");
            }

            if (station.Status != StationStatus.Active)
            {
                throw AuthSupport.CreateValidationException(nameof(CreateCustomBookingItineraryStopRequest.StationId), "Điểm ghé không hoạt động.");
            }
        }

        return stations;
    }
}
