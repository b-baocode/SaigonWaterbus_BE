using FluentValidation.Results;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.CustomBookings;

public sealed record UpdateCustomBookingPassengersCommand(
    Guid BookingId,
    IReadOnlyList<CustomBookingPassengerRequest> Passengers)
    : IRequest<UpdateCustomBookingPassengersResult>;

public sealed class UpdateCustomBookingPassengersCommandValidator
    : AbstractValidator<UpdateCustomBookingPassengersCommand>
{
    public UpdateCustomBookingPassengersCommandValidator()
    {
        RuleFor(x => x.BookingId).NotEmpty();
        RuleFor(x => x.Passengers).NotNull();
        RuleForEach(x => x.Passengers).SetValidator(new CustomBookingPassengerRequestValidator());
    }
}

public sealed class UpdateCustomBookingPassengersCommandHandler
    : IRequestHandler<UpdateCustomBookingPassengersCommand, UpdateCustomBookingPassengersResult>
{
    private const string PaidBookingPaymentStatus = "Paid";

    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;
    private readonly TimeProvider _timeProvider;

    public UpdateCustomBookingPassengersCommandHandler(
        IApplicationDbContext context,
        IUserContext userContext,
        TimeProvider timeProvider)
    {
        _context = context;
        _userContext = userContext;
        _timeProvider = timeProvider;
    }

    public async Task<UpdateCustomBookingPassengersResult> Handle(
        UpdateCustomBookingPassengersCommand request,
        CancellationToken cancellationToken)
    {
        var userId = _userContext.UserId
            ?? throw new ValidationException([new ValidationFailure("userId", "User must be authenticated.")]);

        var booking = await _context.Set<CustomBooking>()
            .Include(x => x.Passengers)
            .SingleOrDefaultAsync(x => x.Id == request.BookingId, cancellationToken)
            ?? throw new NotFoundException("Custom booking not found.");

        if (booking.UserId != userId)
        {
            throw new NotFoundException("Custom booking not found.");
        }

        if (booking.BookingStatus is BookingStatus.Cancelled or BookingStatus.Completed or BookingStatus.Refunded)
        {
            throw new ValidationException([new ValidationFailure(nameof(booking.BookingStatus),
                "Không thể cập nhật danh sách hành khách cho booking đã hủy hoặc đã hoàn tất.")]);
        }

        if (!string.Equals(booking.PaymentStatus, PaidBookingPaymentStatus, StringComparison.OrdinalIgnoreCase))
        {
            throw new ValidationException([new ValidationFailure(nameof(booking.PaymentStatus),
                "Chỉ nhập danh sách hành khách sau khi custom booking đã thanh toán đủ.")]);
        }

        if (request.Passengers.Count > booking.PassengerCount)
        {
            throw new ValidationException([new ValidationFailure(nameof(request.Passengers),
                "Danh sách hành khách không được vượt quá số khách đã đăng ký.")]);
        }

        var today = DateOnly.FromDateTime(_timeProvider.GetUtcNow().UtcDateTime);

        _context.Set<BookingPassenger>().RemoveRange(booking.Passengers);
        var passengers = request.Passengers
            .Select(x => CustomBookingPassengerSupport.ToEntity(booking.Id, x, today))
            .ToList();
        CustomBookingPassengerSupport.EnsurePassengerTypeCountsMatchRequest(
            booking,
            passengers,
            nameof(request.Passengers));
        booking.Passengers = passengers;
        var ticket = await CustomBookingTicketSupport.EnsureBookingLevelTicketAsync(
            _context,
            booking,
            _timeProvider,
            cancellationToken);

        await _context.SaveChangesAsync(cancellationToken);

        var adultCount = CustomBookingPassengerSupport.CountAdults(booking.Passengers);
        var childCount = CustomBookingPassengerSupport.CountChildren(booking.Passengers);

        return new UpdateCustomBookingPassengersResult(
            booking.Id,
            booking.PassengerCount,
            booking.Passengers.Count,
            adultCount,
            childCount,
            booking.Passengers
                .OrderBy(x => x.FullName)
                .Select(CustomBookingPassengerSupport.ToDto)
                .ToList(),
            ticket is null ? null : CustomBookingTicketSupport.ToDto(ticket));
    }
}
