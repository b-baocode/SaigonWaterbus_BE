using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Application.Promotions;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.CharterBookings;

public sealed record CreateCharterBookingCommand(
    DateOnly DepartureDate,
    BoatRentalUnit RentalUnit,
    int DurationValue,
    int AdultCount,
    int ChildCount,
    TimeOnly? StartTime = null,
    Guid? FromStationId = null,
    Guid? ToStationId = null,
    IReadOnlyList<CreateCharterBookingItineraryStopRequest>? ItineraryStops = null,
    SeatSetupType? PreferredSeatSetupType = null,
    string? BoatRequirements = null,
    string? PromotionCode = null,
    string? SpecialRequests = null) : IRequest<CreateCharterBookingResult>;

public sealed class CreateCharterBookingCommandValidator : AbstractValidator<CreateCharterBookingCommand>
{
    public CreateCharterBookingCommandValidator()
    {
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
        RuleFor(x => x.BoatRequirements).MaximumLength(1000).When(x => x.BoatRequirements is not null);
        RuleFor(x => x.PromotionCode).MaximumLength(50).When(x => x.PromotionCode is not null);
        RuleFor(x => x.SpecialRequests).MaximumLength(1000).When(x => x.SpecialRequests is not null);
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

public sealed class CreateCharterBookingCommandHandler
    : IRequestHandler<CreateCharterBookingCommand, CreateCharterBookingResult>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;
    private readonly IBookingCodeGenerator _bookingCodeGenerator;
    private readonly TimeProvider _timeProvider;

    public CreateCharterBookingCommandHandler(
        IApplicationDbContext context,
        IUserContext userContext,
        IBookingCodeGenerator bookingCodeGenerator,
        TimeProvider timeProvider)
    {
        _context = context;
        _userContext = userContext;
        _bookingCodeGenerator = bookingCodeGenerator;
        _timeProvider = timeProvider;
    }

    public async Task<CreateCharterBookingResult> Handle(
        CreateCharterBookingCommand request, CancellationToken cancellationToken)
    {
        var userId = _userContext.UserId
            ?? throw new ValidationException([new ValidationFailure("userId", "User must be authenticated.")]);

        var now = _timeProvider.GetUtcNow();
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var minimumDepartureDate = today.AddDays(7);
        if (request.DepartureDate < minimumDepartureDate)
            throw new ValidationException([new ValidationFailure(nameof(request.DepartureDate),
                $"Charter booking phải được đặt trước ít nhất 7 ngày. Ngày khởi hành sớm nhất là {minimumDepartureDate:dd/MM/yyyy}.")]);

        var passengerCount = request.AdultCount + request.ChildCount;
        const decimal subtotal = 0;

        await EnsureStationExistsAsync(request.FromStationId, nameof(request.FromStationId), cancellationToken);
        await EnsureStationExistsAsync(request.ToStationId, nameof(request.ToStationId), cancellationToken);
        await EnsureItineraryStationsExistAsync(request.ItineraryStops, cancellationToken);

        var promotion = await CharterBookingPricingSupport.ResolvePromotionAsync(
            _context,
            request.PromotionCode,
            subtotal,
            now,
            nameof(request.PromotionCode),
            validateMinOrder: subtotal > 0,
            cancellationToken);
        if (promotion is not null)
        {
            await PromotionUsageSupport.EnsureAccountCanUsePromotionAsync(
                _context,
                promotion,
                userId,
                nameof(request.PromotionCode),
                cancellationToken);
        }
        var discount = CharterBookingPricingSupport.CalculateDiscount(promotion, subtotal);
        var total = subtotal - discount;

        var user = await _context.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == userId, cancellationToken)
            ?? throw new ValidationException([new ValidationFailure("userId", "User không tồn tại.")]);

        var booking = new Booking
        {
            BookingType = Booking.CharterBookingType,
            UserId = userId,
            FromStationId = request.FromStationId,
            ToStationId = request.ToStationId,
            DepartureDate = request.DepartureDate,
            StartTime = request.StartTime,
            RentalUnit = request.RentalUnit,
            DurationValue = request.DurationValue,
            PassengerCount = passengerCount,
            AdultCount = request.AdultCount,
            ChildCount = request.ChildCount,
            PreferredSeatSetupType = request.PreferredSeatSetupType,
            BoatRequirements = request.BoatRequirements?.Trim(),
            SpecialRequests = request.SpecialRequests?.Trim(),
            PromotionId = promotion?.Id,
            BookingCode = await _bookingCodeGenerator.GenerateAsync(cancellationToken),
            ContactName = user.FullName,
            ContactPhone = user.PhoneNumber ?? string.Empty,
            ContactEmail = user.Email,
            BookingStatus = BookingStatus.PendingQuote,
            SubtotalAmount = subtotal,
            DiscountAmount = discount,
            TotalAmount = total,
            RemainingAmount = total,
            ItineraryStops = request.ItineraryStops?
                .OrderBy(x => x.StopOrder)
                .Select(x => new BookingItineraryStop
                {
                    StationId = x.StationId,
                    StopOrder = x.StopOrder,
                    StayDurationMinutes = x.StayDurationMinutes,
                    Note = x.Note?.Trim()
                })
                .ToList() ?? []
        };

        _context.Set<Booking>().Add(booking);

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            throw new ValidationException([new ValidationFailure(nameof(request.DepartureDate),
                "Tạo yêu cầu thuê tàu thất bại. Vui lòng thử lại.")]);
        }

        return new CreateCharterBookingResult(
            booking.Id,
            booking.BookingCode,
            null,
            booking.SubtotalAmount,
            booking.DiscountAmount,
            booking.TotalAmount,
            booking.BookingStatus.ToString(),
            0,
            promotion?.PromotionCode);
    }

    private async Task EnsureStationExistsAsync(Guid? stationId, string field, CancellationToken cancellationToken)
    {
        if (stationId is null)
            return;

        var exists = await _context.Set<Station>()
            .AnyAsync(s => s.Id == stationId.Value, cancellationToken);
        if (!exists)
            throw new ValidationException([new ValidationFailure(field, "Bến không tồn tại.")]);
    }

    private async Task EnsureItineraryStationsExistAsync(
        IReadOnlyList<CreateCharterBookingItineraryStopRequest>? stops,
        CancellationToken cancellationToken)
    {
        if (stops is null || stops.Count == 0)
            return;

        var stationIds = stops.Select(x => x.StationId).Distinct().ToArray();
        var existingStationIds = await _context.Set<Station>()
            .Where(s => stationIds.Contains(s.Id))
            .Select(s => s.Id)
            .ToListAsync(cancellationToken);

        var missingStationId = stationIds.Except(existingStationIds).FirstOrDefault();
        if (missingStationId != Guid.Empty)
            throw new ValidationException([new ValidationFailure(nameof(CreateCharterBookingCommand.ItineraryStops),
                $"Điểm dừng có stationId '{missingStationId}' không tồn tại.")]);
    }
}

public sealed class CharterBookingPassengerRequestValidator : AbstractValidator<CharterBookingPassengerRequest>
{
    public CharterBookingPassengerRequestValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(150);
        RuleFor(x => x.DateOfBirth)
            .NotEmpty()
            .Must(x => CharterBookingPassengerSupport.TryParseDateOfBirth(x, out _))
            .WithMessage("Ngày sinh không hợp lệ. Dùng định dạng yyyy-MM-dd hoặc dd/MM/yyyy.");
        RuleFor(x => x.DateOfBirth)
            .Must(x => !CharterBookingPassengerSupport.TryParseDateOfBirth(x, out var dateOfBirth)
                || dateOfBirth <= DateOnly.FromDateTime(DateTime.UtcNow))
            .WithMessage("Ngày sinh không được ở tương lai.");
    }
}
