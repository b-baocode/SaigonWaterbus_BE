using FluentValidation.Results;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.CharterBookings;

public sealed record UpdateCharterBookingCommand(
    Guid BookingId,
    DateOnly DepartureDate,
    BoatRentalUnit RentalUnit,
    int DurationValue,
    int AdultCount,
    int ChildCount,
    TimeOnly? StartTime = null,
    Guid? FromStationId = null,
    Guid? ToStationId = null,
    IReadOnlyList<CreateCharterBookingItineraryStopRequest>? ItineraryStops = null,
    IReadOnlyList<CreateCharterBookingBoatRequest>? RequestedBoats = null,
    SeatSetupType? PreferredSeatSetupType = null,
    string? BoatRequirements = null,
    string? SpecialRequests = null,
    string? ContactEmail = null) : IRequest<CharterBookingDetailDto>;

public sealed class UpdateCharterBookingCommandValidator : AbstractValidator<UpdateCharterBookingCommand>
{
    public UpdateCharterBookingCommandValidator()
    {
        RuleFor(x => x.BookingId).NotEmpty();
        RuleFor(x => x.DepartureDate).NotEqual(default(DateOnly)).WithMessage("Ngày khởi hành là bắt buộc.");
        RuleFor(x => x.RentalUnit).IsInEnum().WithMessage("Đơn vị thuê tàu chỉ được là Hour hoặc Day.");
        RuleFor(x => x.DurationValue).GreaterThan(0).LessThanOrEqualTo(60)
            .WithMessage("Thời lượng thuê phải từ 1 đến 60.");
        RuleFor(x => x.AdultCount).GreaterThanOrEqualTo(0).LessThanOrEqualTo(1000)
            .WithMessage("Số người lớn phải từ 0 đến 1000.");
        RuleFor(x => x.ChildCount).GreaterThanOrEqualTo(0).LessThanOrEqualTo(1000)
            .WithMessage("Số trẻ em phải từ 0 đến 1000.");
        RuleFor(x => x)
            .Must(x => x.AdultCount + x.ChildCount > 0)
            .WithMessage("Tổng số khách phải lớn hơn 0.");
        RuleFor(x => x)
            .Must(x => x.AdultCount + x.ChildCount <= 1000)
            .WithMessage("Tổng số khách không được vượt quá 1000.");
        RuleFor(x => x.PreferredSeatSetupType).IsInEnum().When(x => x.PreferredSeatSetupType.HasValue);
        RuleFor(x => x.RequestedBoats)
            .Must(x => x is null || x.Count > 0)
            .WithMessage("Danh sách tàu yêu cầu không được rỗng nếu được gửi.");
        RuleFor(x => x.RequestedBoats)
            .Must(x => x is null || x.Count <= CharterBookingBoatSelectionSupport.MaxRequestedBoatCount)
            .WithMessage($"Số lượng tàu yêu cầu không được vượt quá {CharterBookingBoatSelectionSupport.MaxRequestedBoatCount}.");
        RuleForEach(x => x.RequestedBoats).ChildRules(boat =>
        {
            boat.RuleFor(x => x.SeatSetupType)
                .IsInEnum()
                .WithMessage("Kiểu tàu không hợp lệ.");
        });
        RuleFor(x => x.BoatRequirements).MaximumLength(1000).When(x => x.BoatRequirements is not null);
        RuleFor(x => x.SpecialRequests).MaximumLength(1000).When(x => x.SpecialRequests is not null);
        RuleFor(x => x.ContactEmail)
            .MaximumLength(255)
            .EmailAddress()
            .When(x => !string.IsNullOrWhiteSpace(x.ContactEmail))
            .WithMessage("Email nhận thông tin charter booking không hợp lệ.");
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

    public UpdateCharterBookingCommandHandler(
        IApplicationDbContext context,
        IUserContext userContext,
        TimeProvider timeProvider)
    {
        _context = context;
        _userContext = userContext;
        _timeProvider = timeProvider;
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

        EnsureCanUpdate(booking);
        EnsureDepartureDateCanBeUpdated(booking, request.DepartureDate);
        await EnsureStationExistsAsync(request.FromStationId, nameof(request.FromStationId), cancellationToken);
        await EnsureStationExistsAsync(request.ToStationId, nameof(request.ToStationId), cancellationToken);
        await EnsureItineraryStationsExistAsync(request.ItineraryStops, cancellationToken);

        var requestedBoatTypes = CharterBookingBoatSelectionSupport.NormalizeRequestedBoatTypes(
            request.RequestedBoats,
            request.PreferredSeatSetupType);

        booking.FromStationId = request.FromStationId;
        booking.ToStationId = request.ToStationId;
        booking.DepartureDate = request.DepartureDate;
        booking.StartTime = request.StartTime;
        booking.RentalUnit = request.RentalUnit;
        booking.DurationValue = request.DurationValue;
        booking.AdultCount = request.AdultCount;
        booking.ChildCount = request.ChildCount;
        booking.PassengerCount = request.AdultCount + request.ChildCount;
        booking.RequestedBoatCount = requestedBoatTypes.Count == 0 ? null : requestedBoatTypes.Count;
        booking.RequestedBoatTypes = CharterBookingBoatSelectionSupport.ToStorageValue(requestedBoatTypes);
        booking.PreferredSeatSetupType = request.PreferredSeatSetupType
            ?? CharterBookingBoatSelectionSupport.FirstOrNull(requestedBoatTypes);
        booking.BoatRequirements = request.BoatRequirements?.Trim();
        booking.SpecialRequests = request.SpecialRequests?.Trim();

        var contactEmail = NormalizeContactEmail(request.ContactEmail);
        if (contactEmail is not null)
        {
            booking.ContactEmail = contactEmail;
        }

        _context.Set<BookingItineraryStop>().RemoveRange(booking.ItineraryStops);
        booking.ItineraryStops = request.ItineraryStops?
            .OrderBy(x => x.StopOrder)
            .Select(x => new BookingItineraryStop
            {
                BookingId = booking.Id,
                StationId = x.StationId,
                StopOrder = x.StopOrder,
                StayDurationMinutes = x.StayDurationMinutes,
                Note = x.Note?.Trim()
            })
            .ToList() ?? [];

        await _context.SaveChangesAsync(cancellationToken);

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

    private static string? NormalizeContactEmail(string? contactEmail) =>
        string.IsNullOrWhiteSpace(contactEmail) ? null : contactEmail.Trim();
}
