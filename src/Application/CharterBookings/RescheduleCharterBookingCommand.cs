using FluentValidation.Results;
using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.CharterBookings;

/// <summary>Admin override cho lịch charter chưa kết thúc; đồng bộ các trip charter đã được sinh.</summary>
public sealed record RescheduleCharterBookingCommand(
    Guid BookingId,
    DateOnly DepartureDate,
    TimeOnly StartTime) : IRequest<CharterBookingDetailDto>;

public sealed class RescheduleCharterBookingCommandValidator
    : AbstractValidator<RescheduleCharterBookingCommand>
{
    public RescheduleCharterBookingCommandValidator()
    {
        RuleFor(x => x.BookingId).NotEmpty();
        RuleFor(x => x.DepartureDate).NotEqual(default(DateOnly));
    }
}

public sealed class RescheduleCharterBookingCommandHandler
    : IRequestHandler<RescheduleCharterBookingCommand, CharterBookingDetailDto>
{
    private static readonly TimeSpan VietnamOffset = TimeSpan.FromHours(7);

    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;
    private readonly TimeProvider _timeProvider;
    private readonly ICharterBookingRealtimeNotifier _realtimeNotifier;

    public RescheduleCharterBookingCommandHandler(
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
        RescheduleCharterBookingCommand request,
        CancellationToken cancellationToken)
    {
        await AuthSupport.EnsureCurrentUserIsAdminAsync(_context, _userContext, cancellationToken);

        var booking = await CharterBookingQuerySupport.BuildDetailQuery(_context)
            .SingleOrDefaultAsync(x => x.Id == request.BookingId, cancellationToken)
            ?? throw new NotFoundException("Charter booking not found.");

        if (booking.BookingStatus is BookingStatus.Cancelled or BookingStatus.Expired or BookingStatus.Completed)
        {
            throw new ValidationException([new ValidationFailure(nameof(request.BookingId),
                "Không thể đổi lịch charter booking đã hủy, hết hạn hoặc hoàn tất.")]);
        }

        var newDeparture = new DateTimeOffset(request.DepartureDate.ToDateTime(request.StartTime), VietnamOffset)
            .ToUniversalTime();
        if (newDeparture <= _timeProvider.GetUtcNow())
        {
            throw new ValidationException([new ValidationFailure(nameof(request.DepartureDate),
                "Ngày và giờ khởi hành mới phải ở tương lai.")]);
        }

        var trips = await _context.Set<Trip>()
            .Include(x => x.TripStops)
            .Where(x => x.SourceBookingId == booking.Id)
            .ToListAsync(cancellationToken);
        if (trips.Any(x => x.TripStatus is TripStatus.InProgress or TripStatus.Completed or TripStatus.Cancelled))
        {
            throw new ValidationException([new ValidationFailure(nameof(request.BookingId),
                "Không thể đổi lịch khi charter trip đã chạy, hoàn tất hoặc bị hủy.")]);
        }

        var primaryTrip = booking.TripId.HasValue
            ? trips.SingleOrDefault(x => x.Id == booking.TripId.Value)
            : null;
        var currentScheduleDeparture = primaryTrip?.DepartureTime
            ?? trips.OrderBy(x => x.DepartureTime).FirstOrDefault()?.DepartureTime
            ?? CharterBookingTripSupport.ResolveDepartureTimeUtc(booking);
        var shift = newDeparture - currentScheduleDeparture;
        booking.DepartureDate = request.DepartureDate;
        booking.StartTime = request.StartTime;

        foreach (var trip in trips)
        {
            trip.OperatingDate = request.DepartureDate;
            trip.DepartureTime = trip.DepartureTime.Add(shift);
            trip.ArrivalTime = trip.ArrivalTime.Add(shift);
            foreach (var stop in trip.TripStops)
            {
                stop.PlannedArrivalTime = Shift(stop.PlannedArrivalTime, shift);
                stop.PlannedDepartureTime = Shift(stop.PlannedDepartureTime, shift);
                stop.AdjustedArrivalTime = Shift(stop.AdjustedArrivalTime, shift);
                stop.AdjustedDepartureTime = Shift(stop.AdjustedDepartureTime, shift);
            }
        }

        await _context.SaveChangesAsync(cancellationToken);
        await _realtimeNotifier.PublishChangedAsync(
            new CharterBookingRealtimeEvent(
                booking.Id,
                "RescheduledByAdmin",
                booking.BookingStatus.ToString(),
                booking.PaymentStatus,
                _timeProvider.GetUtcNow()),
            cancellationToken);

        var updatedBooking = await CharterBookingQuerySupport.BuildDetailQuery(_context)
            .SingleAsync(x => x.Id == booking.Id, cancellationToken);
        var relatedRoutes = await CharterBookingRoutePricingSupport.LoadRelatedRoutesAsync(
            _context, updatedBooking, cancellationToken);
        return CharterBookingQuerySupport.ToDetailDto(updatedBooking, relatedRoutes);
    }

    private static DateTimeOffset? Shift(DateTimeOffset? value, TimeSpan shift) =>
        value?.Add(shift);
}
