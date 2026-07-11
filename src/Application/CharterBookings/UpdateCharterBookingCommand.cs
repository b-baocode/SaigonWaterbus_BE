using FluentValidation.Results;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.CharterBookings;

public sealed record UpdateCharterBookingCommand(
    Guid BookingId,
    DateOnly? DepartureDate = null,
    BoatRentalUnit? RentalUnit = null,
    int? DurationValue = null,
    int? AdultCount = null,
    int? ChildCount = null,
    TimeOnly? StartTime = null,
    Guid? FromStationId = null,
    Guid? ToStationId = null,
    IReadOnlyList<CreateCharterBookingItineraryStopRequest>? ItineraryStops = null,
    IReadOnlyList<CreateCharterBookingBoatRequest>? RequestedBoats = null,
    string? SpecialRequests = null,
    string? ContactName = null,
    string? ContactPhone = null,
    string? ContactEmail = null,
    bool? InsuranceSelected = null,
    Guid? InsurancePackageId = null) : IRequest<CharterBookingDetailDto>;

public sealed class UpdateCharterBookingCommandValidator : AbstractValidator<UpdateCharterBookingCommand>
{
    public UpdateCharterBookingCommandValidator()
    {
        RuleFor(x => x.BookingId).NotEmpty();
        RuleFor(x => x.DepartureDate!.Value)
            .NotEqual(default(DateOnly))
            .When(x => x.DepartureDate.HasValue)
            .WithMessage("Ngày khởi hành là bắt buộc.");
        RuleFor(x => x.RentalUnit!.Value)
            .IsInEnum()
            .When(x => x.RentalUnit.HasValue)
            .WithMessage("Đơn vị thuê tàu chỉ được là Hour hoặc Day.");
        RuleFor(x => x.DurationValue!.Value).GreaterThan(0).LessThanOrEqualTo(60)
            .When(x => x.DurationValue.HasValue)
            .WithMessage("Thời lượng thuê phải từ 1 đến 60.");
        RuleFor(x => x.AdultCount!.Value).GreaterThanOrEqualTo(0).LessThanOrEqualTo(1000)
            .When(x => x.AdultCount.HasValue)
            .WithMessage("Số người lớn phải từ 0 đến 1000.");
        RuleFor(x => x.ChildCount!.Value).GreaterThanOrEqualTo(0).LessThanOrEqualTo(1000)
            .When(x => x.ChildCount.HasValue)
            .WithMessage("Số trẻ em phải từ 0 đến 1000.");
        RuleFor(x => x)
            .Must(x => !x.AdultCount.HasValue || !x.ChildCount.HasValue || x.AdultCount + x.ChildCount > 0)
            .WithMessage("Tổng số khách phải lớn hơn 0.");
        RuleFor(x => x)
            .Must(x => !x.AdultCount.HasValue || !x.ChildCount.HasValue || x.AdultCount + x.ChildCount <= 1000)
            .WithMessage("Tổng số khách không được vượt quá 1000.");
        RuleFor(x => x.RequestedBoats)
            .Must(x => x is null || x.Count > 0)
            .WithMessage("Danh sách tàu yêu cầu không được rỗng nếu được gửi.");
        RuleFor(x => x.RequestedBoats)
            .Must(x => x is null || x.Count <= CharterBookingBoatSelectionSupport.MaxRequestedBoatCount)
            .WithMessage($"Số lượng tàu yêu cầu không được vượt quá {CharterBookingBoatSelectionSupport.MaxRequestedBoatCount}.");
        RuleForEach(x => x.RequestedBoats).ChildRules(boat =>
        {
            boat.RuleFor(x => x.NumberOfDecks)
                .GreaterThan(0)
                .WithMessage("Số tầng tàu yêu cầu phải lớn hơn 0.");
        });
        RuleFor(x => x.SpecialRequests).MaximumLength(1000).When(x => x.SpecialRequests is not null);
        RuleFor(x => x.ContactName)
            .MaximumLength(150)
            .When(x => !string.IsNullOrWhiteSpace(x.ContactName));
        RuleFor(x => x.ContactPhone)
            .MaximumLength(30)
            .When(x => !string.IsNullOrWhiteSpace(x.ContactPhone));
        RuleFor(x => x.ContactEmail)
            .MaximumLength(255)
            .EmailAddress()
            .When(x => !string.IsNullOrWhiteSpace(x.ContactEmail))
            .WithMessage("Email nhận thông tin charter booking không hợp lệ.");
        RuleFor(x => x.InsurancePackageId)
            .NotEmpty()
            .When(x => x.InsurancePackageId.HasValue)
            .WithMessage("Gói bảo hiểm không hợp lệ.");
        RuleFor(x => x.InsurancePackageId)
            .NotNull()
            .When(x => x.InsuranceSelected == true)
            .WithMessage("Vui lòng chọn gói bảo hiểm.");
        RuleFor(x => x.ToStationId).NotEqual(x => x.FromStationId)
            .When(x => x.FromStationId.HasValue && x.ToStationId.HasValue)
            .WithMessage("Bến đi và bến đến phải khác nhau.");
        RuleFor(x => x.ItineraryStops)
            .Must(stops => stops is null || stops.Count <= 50)
            .WithMessage("Hành trình không được vượt quá 50 điểm dừng.");
        RuleFor(x => x.ItineraryStops)
            .Must(HaveUniqueStopOrders)
            .When(x => x.ItineraryStops is { Count: > 0 })
            .WithMessage("Thứ tự điểm dừng không được trùng nhau.");
        RuleForEach(x => x.ItineraryStops).ChildRules(stop =>
        {
            stop.RuleFor(x => x.StationId).NotEqual(Guid.Empty).WithMessage("StationId của điểm dừng không hợp lệ.");
            stop.RuleFor(x => x.StopOrder).GreaterThan(0).WithMessage("StopOrder phải lớn hơn 0.");
            stop.RuleFor(x => x.StayDurationMinutes)
                .InclusiveBetween(0, 1440)
                .WithMessage("Thời gian dừng phải từ 0 đến 1440 phút.");
            stop.RuleFor(x => x.Note).MaximumLength(500).When(x => x.Note is not null);
        });
    }

    private static bool HaveUniqueStopOrders(IReadOnlyList<CreateCharterBookingItineraryStopRequest>? stops) =>
        stops is null || stops.Select(x => x.StopOrder).Distinct().Count() == stops.Count;
}

public sealed class UpdateCharterBookingCommandHandler
    : IRequestHandler<UpdateCharterBookingCommand, CharterBookingDetailDto>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;
    private readonly TimeProvider _timeProvider;
    private readonly ICharterBookingRealtimeNotifier _realtimeNotifier;

    public UpdateCharterBookingCommandHandler(
        IApplicationDbContext context,
        IUserContext userContext,
        TimeProvider timeProvider,
        ICharterBookingRealtimeNotifier? realtimeNotifier = null)
    {
        _context = context;
        _userContext = userContext;
        _timeProvider = timeProvider;
        _realtimeNotifier = realtimeNotifier ?? NullCharterBookingRealtimeNotifier.Instance;
    }

    public async Task<CharterBookingDetailDto> Handle(
        UpdateCharterBookingCommand request,
        CancellationToken cancellationToken)
    {
        var userId = _userContext.UserId
            ?? throw new ValidationException([new ValidationFailure("userId", "User must be authenticated.")]);

        var booking = await CharterBookingQuerySupport.BuildBaseQuery(_context)
            .Include(x => x.ItineraryStops)
            .Include(x => x.Payments)
            .SingleOrDefaultAsync(x => x.Id == request.BookingId, cancellationToken)
            ?? throw new NotFoundException("Charter booking not found.");

        if (booking.UserId != userId)
        {
            throw new NotFoundException("Charter booking not found.");
        }
        var user = await _context.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == userId, cancellationToken)
            ?? throw new ValidationException([new ValidationFailure("userId", "User không tồn tại.")]);

        EnsureCanUpdate(booking);
        var departureDate = ResolveRequiredValue(
            request.DepartureDate,
            booking.DepartureDate,
            nameof(request.DepartureDate),
            "Ngày khởi hành là bắt buộc.");
        var rentalUnit = request.RentalUnit ?? booking.RentalUnit;
        var durationValue = request.DurationValue ?? booking.DurationValue;
        var adultCount = request.AdultCount ?? booking.AdultCount.GetValueOrDefault();
        var childCount = request.ChildCount ?? booking.ChildCount.GetValueOrDefault();
        var startTime = request.StartTime ?? booking.StartTime;
        var fromStationId = request.FromStationId ?? booking.FromStationId;
        var toStationId = request.ToStationId ?? booking.ToStationId;
        var requestedBoatDecks = request.RequestedBoats is null
            ? CharterBookingBoatSelectionSupport.FromDeckStorageValue(booking.RequestedBoatDecks)
            : CharterBookingBoatSelectionSupport.NormalizeRequestedBoatDecks(request.RequestedBoats);
        var requestedBoatCount = CharterBookingBoatSelectionSupport.ResolveRequestedBoatCount(requestedBoatDecks);
        var requestedBoatDeckStorage = request.RequestedBoats is null
            ? booking.RequestedBoatDecks
            : CharterBookingBoatSelectionSupport.ToStorageValue(requestedBoatDecks);
        var itineraryStops = request.ItineraryStops is null
            ? CharterBookingDuplicateSupport.ToItineraryStops(booking.ItineraryStops)
            : CharterBookingDuplicateSupport.ToItineraryStops(request.ItineraryStops);

        ValidateResolvedUpdateValues(durationValue, adultCount, childCount, fromStationId, toStationId);
        ValidateRequestedBoats(request.RequestedBoats);
        ValidateItineraryStops(request.ItineraryStops);
        EnsureDepartureDateCanBeUpdated(booking, departureDate);
        await CharterBookingStationValidationSupport.EnsureWaterbusDepartureStationAsync(
            _context,
            fromStationId,
            nameof(request.FromStationId),
            cancellationToken);
        await EnsureStationExistsAsync(toStationId, nameof(request.ToStationId), cancellationToken);
        await EnsureItineraryStationsExistAsync(request.ItineraryStops, cancellationToken);

        var contactName = ResolveRequiredContactValue(
            request.ContactName,
            booking.ContactName,
            user.FullName,
            nameof(request.ContactName),
            "Họ tên người đặt là bắt buộc.");
        var contactPhone = ResolveRequiredContactValue(
            request.ContactPhone,
            booking.ContactPhone,
            user.PhoneNumber,
            nameof(request.ContactPhone),
            "Số điện thoại người đặt là bắt buộc.");
        var contactEmail = ResolveRequiredContactValue(
            request.ContactEmail,
            booking.ContactEmail,
            user.Email,
            nameof(request.ContactEmail),
            "Email nhận thông tin charter booking là bắt buộc.");

        await CharterBookingDuplicateSupport.EnsureNoDuplicateActiveRequestAsync(
            _context,
            userId,
            booking.Id,
            departureDate,
            startTime,
            rentalUnit,
            durationValue,
            fromStationId,
            toStationId,
            adultCount,
            childCount,
            requestedBoatDeckStorage,
            itineraryStops,
            contactPhone,
            contactEmail,
            cancellationToken);
        var insuranceSnapshot = await CharterBookingInsuranceSupport.ResolveRequestedInsuranceSnapshotAsync(
            _context,
            request.InsuranceSelected,
            request.InsurancePackageId,
            booking.InsuranceSnapshot,
            adultCount + childCount,
            _timeProvider.GetUtcNow(),
            cancellationToken);

        booking.FromStationId = fromStationId;
        booking.ToStationId = toStationId;
        booking.DepartureDate = departureDate;
        booking.StartTime = startTime;
        booking.RentalUnit = rentalUnit;
        booking.DurationValue = durationValue;
        booking.AdultCount = adultCount;
        booking.ChildCount = childCount;
        booking.PassengerCount = adultCount + childCount;
        if (request.RequestedBoats is not null)
        {
            booking.RequestedBoatCount = requestedBoatCount == 0 ? null : requestedBoatCount;
            booking.RequestedBoatDecks = requestedBoatDeckStorage;
            booking.RequestedBoatTypes = null;
            booking.PreferredSeatSetupType = null;
        }
        booking.SpecialRequests = ResolveOptionalText(request.SpecialRequests, booking.SpecialRequests);
        booking.ContactName = contactName;
        booking.ContactPhone = contactPhone;
        booking.ContactEmail = contactEmail;
        booking.InsuranceSnapshot = insuranceSnapshot;

        if (request.ItineraryStops is not null)
        {
            ApplyItineraryStops(booking, request.ItineraryStops);
        }

        await _context.SaveChangesAsync(cancellationToken);
        await _realtimeNotifier.PublishChangedAsync(
            new CharterBookingRealtimeEvent(
                booking.Id,
                "Updated",
                booking.BookingStatus.ToString(),
                booking.PaymentStatus,
                _timeProvider.GetUtcNow()),
            cancellationToken);

        var updatedBooking = await CharterBookingQuerySupport.BuildDetailQuery(_context)
            .AsNoTracking()
            .SingleAsync(x => x.Id == booking.Id, cancellationToken);
        var relatedRoutes = await CharterBookingRoutePricingSupport.LoadRelatedRoutesAsync(
            _context,
            updatedBooking,
            cancellationToken);

        return CharterBookingQuerySupport.ToDetailDto(updatedBooking, relatedRoutes);
    }

    private void EnsureDepartureDateCanBeUpdated(Booking booking, DateOnly requestedDepartureDate)
    {
        if (booking.DepartureDate == requestedDepartureDate)
        {
            return;
        }

        var now = _timeProvider.GetUtcNow();
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var minimumDepartureDate = today.AddDays(7);
        if (requestedDepartureDate < minimumDepartureDate)
        {
            throw new ValidationException([new ValidationFailure(nameof(requestedDepartureDate),
                $"Charter booking phải được đặt trước ít nhất 7 ngày. Ngày khởi hành sớm nhất là {minimumDepartureDate:dd/MM/yyyy}.")]);
        }
    }

    private static void EnsureCanUpdate(Booking booking)
    {
        if (booking.BookingStatus != BookingStatus.PendingQuote)
        {
            throw new ValidationException([new ValidationFailure(nameof(booking.BookingStatus),
                "Chỉ có thể chỉnh sửa yêu cầu thuê tàu khi booking đang chờ báo giá.")]);
        }

        if (booking.Payments.Any(x =>
                string.Equals(x.PaymentStatus, "Pending", StringComparison.OrdinalIgnoreCase)
                || string.Equals(x.PaymentStatus, "Paid", StringComparison.OrdinalIgnoreCase)))
        {
            throw new ValidationException([new ValidationFailure(nameof(booking.Payments),
                "Booking đã có payment đang chờ hoặc đã thanh toán nên không thể chỉnh sửa.")]);
        }
    }

    private static void ValidateResolvedUpdateValues(
        int? durationValue,
        int adultCount,
        int childCount,
        Guid? fromStationId,
        Guid? toStationId)
    {
        if (durationValue is <= 0 or > 60)
        {
            throw new ValidationException([new ValidationFailure(nameof(UpdateCharterBookingCommand.DurationValue),
                "Thời lượng thuê phải từ 1 đến 60.")]);
        }

        if (adultCount is < 0 or > 1000)
        {
            throw new ValidationException([new ValidationFailure(nameof(UpdateCharterBookingCommand.AdultCount),
                "Số người lớn phải từ 0 đến 1000.")]);
        }

        if (childCount is < 0 or > 1000)
        {
            throw new ValidationException([new ValidationFailure(nameof(UpdateCharterBookingCommand.ChildCount),
                "Số trẻ em phải từ 0 đến 1000.")]);
        }

        var passengerCount = adultCount + childCount;
        if (passengerCount <= 0)
        {
            throw new ValidationException([new ValidationFailure(nameof(UpdateCharterBookingCommand.AdultCount),
                "Tổng số khách phải lớn hơn 0.")]);
        }

        if (passengerCount > 1000)
        {
            throw new ValidationException([new ValidationFailure(nameof(UpdateCharterBookingCommand.AdultCount),
                "Tổng số khách không được vượt quá 1000.")]);
        }

        if (fromStationId.HasValue && toStationId.HasValue && fromStationId.Value == toStationId.Value)
        {
            throw new ValidationException([new ValidationFailure(nameof(UpdateCharterBookingCommand.ToStationId),
                "Bến đi và bến đến phải khác nhau.")]);
        }
    }

    private static void ValidateRequestedBoats(IReadOnlyList<CreateCharterBookingBoatRequest>? requestedBoats)
    {
        if (requestedBoats is null)
        {
            return;
        }

        if (requestedBoats.Count == 0)
        {
            throw new ValidationException([new ValidationFailure(nameof(UpdateCharterBookingCommand.RequestedBoats),
                "Danh sách tàu yêu cầu không được rỗng nếu được gửi.")]);
        }

        if (requestedBoats.Count > CharterBookingBoatSelectionSupport.MaxRequestedBoatCount)
        {
            throw new ValidationException([new ValidationFailure(nameof(UpdateCharterBookingCommand.RequestedBoats),
                $"Số lượng tàu yêu cầu không được vượt quá {CharterBookingBoatSelectionSupport.MaxRequestedBoatCount}.")]);
        }

        if (requestedBoats.Any(x => x.NumberOfDecks <= 0))
        {
            throw new ValidationException([new ValidationFailure(nameof(UpdateCharterBookingCommand.RequestedBoats),
                "Số tầng tàu yêu cầu phải lớn hơn 0.")]);
        }
    }

    private static void ValidateItineraryStops(
        IReadOnlyList<CreateCharterBookingItineraryStopRequest>? itineraryStops)
    {
        if (itineraryStops is null)
        {
            return;
        }

        if (itineraryStops.Count > 50)
        {
            throw new ValidationException([new ValidationFailure(nameof(UpdateCharterBookingCommand.ItineraryStops),
                "Hành trình không được vượt quá 50 điểm dừng.")]);
        }

        if (itineraryStops.Select(x => x.StopOrder).Distinct().Count() != itineraryStops.Count)
        {
            throw new ValidationException([new ValidationFailure(nameof(UpdateCharterBookingCommand.ItineraryStops),
                "Thứ tự điểm dừng không được trùng nhau.")]);
        }

        foreach (var stop in itineraryStops)
        {
            if (stop.StationId == Guid.Empty)
            {
                throw new ValidationException([new ValidationFailure(nameof(UpdateCharterBookingCommand.ItineraryStops),
                    "StationId của điểm dừng không hợp lệ.")]);
            }

            if (stop.StopOrder <= 0)
            {
                throw new ValidationException([new ValidationFailure(nameof(UpdateCharterBookingCommand.ItineraryStops),
                    "StopOrder phải lớn hơn 0.")]);
            }

            if (stop.StayDurationMinutes is < 0 or > 1440)
            {
                throw new ValidationException([new ValidationFailure(nameof(UpdateCharterBookingCommand.ItineraryStops),
                    "Thời gian dừng phải từ 0 đến 1440 phút.")]);
            }

            if (stop.Note is { Length: > 500 })
            {
                throw new ValidationException([new ValidationFailure(nameof(UpdateCharterBookingCommand.ItineraryStops),
                    "Ghi chú điểm dừng không được vượt quá 500 ký tự.")]);
            }
        }
    }

    private async Task EnsureStationExistsAsync(Guid? stationId, string field, CancellationToken cancellationToken)
    {
        if (stationId is null)
        {
            return;
        }

        var exists = await _context.Set<Station>()
            .AnyAsync(s => s.Id == stationId.Value, cancellationToken);
        if (!exists)
        {
            throw new ValidationException([new ValidationFailure(field, "Bến không tồn tại.")]);
        }
    }

    private async Task EnsureItineraryStationsExistAsync(
        IReadOnlyList<CreateCharterBookingItineraryStopRequest>? stops,
        CancellationToken cancellationToken)
    {
        if (stops is null || stops.Count == 0)
        {
            return;
        }

        var stationIds = stops.Select(x => x.StationId).Distinct().ToArray();
        var existingStationIds = await _context.Set<Station>()
            .Where(s => stationIds.Contains(s.Id))
            .Select(s => s.Id)
            .ToListAsync(cancellationToken);

        var missingStationId = stationIds.Except(existingStationIds).FirstOrDefault();
        if (missingStationId != Guid.Empty)
        {
            throw new ValidationException([new ValidationFailure(nameof(UpdateCharterBookingCommand.ItineraryStops),
                $"Điểm dừng có stationId '{missingStationId}' không tồn tại.")]);
        }
    }

    private static string ResolveRequiredContactValue(
        string? requestedValue,
        string? existingValue,
        string? fallbackValue,
        string field,
        string message)
    {
        var value = NormalizeContactValue(requestedValue)
            ?? NormalizeContactValue(existingValue)
            ?? NormalizeContactValue(fallbackValue);
        if (value is null)
        {
            throw new ValidationException([new ValidationFailure(field, message)]);
        }

        return value;
    }

    private static string? NormalizeContactValue(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static T ResolveRequiredValue<T>(T? requestedValue, T? existingValue, string field, string message)
        where T : struct =>
        requestedValue ?? existingValue
        ?? throw new ValidationException([new ValidationFailure(field, message)]);

    private static string? ResolveOptionalText(string? requestedValue, string? existingValue) =>
        requestedValue is null ? existingValue : NormalizeContactValue(requestedValue);

    private void ApplyItineraryStops(
        Booking booking,
        IReadOnlyList<CreateCharterBookingItineraryStopRequest> requestedStops)
    {
        var requestedByOrder = requestedStops
            .OrderBy(x => x.StopOrder)
            .ToDictionary(x => x.StopOrder);
        var existingByOrder = booking.ItineraryStops.ToDictionary(x => x.StopOrder);

        foreach (var requestedStop in requestedByOrder.Values)
        {
            if (existingByOrder.TryGetValue(requestedStop.StopOrder, out var existingStop))
            {
                existingStop.StationId = requestedStop.StationId;
                existingStop.StayDurationMinutes = requestedStop.StayDurationMinutes;
                existingStop.Note = NormalizeContactValue(requestedStop.Note);
                continue;
            }

            booking.ItineraryStops.Add(new BookingItineraryStop
            {
                BookingId = booking.Id,
                StationId = requestedStop.StationId,
                StopOrder = requestedStop.StopOrder,
                StayDurationMinutes = requestedStop.StayDurationMinutes,
                Note = NormalizeContactValue(requestedStop.Note)
            });
        }

        var requestedOrders = requestedByOrder.Keys.ToHashSet();
        var stopsToRemove = booking.ItineraryStops
            .Where(x => !requestedOrders.Contains(x.StopOrder))
            .ToArray();
        _context.Set<BookingItineraryStop>().RemoveRange(stopsToRemove);
    }
}
