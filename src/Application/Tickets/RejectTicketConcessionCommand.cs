using FluentValidation.Results;
using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Exceptions;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Application.Payments;
using SaigonWaterbus.Application.TicketTypes;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.Tickets;

public sealed record RejectTicketConcessionCommand(
    string CodeOrToken,
    string Reason,
    TicketScanRequestMetadata? Metadata = null) : IRequest<RejectTicketConcessionResult>;

public sealed record RejectTicketConcessionResult(
    TicketScanDto Ticket,
    string Action,
    string OriginalPassengerType,
    string CurrentPassengerType,
    decimal PreviousUnitPrice,
    decimal CurrentUnitPrice,
    decimal AdditionalAmount,
    bool RequiresAdditionalPayment,
    decimal BookingTotalAmount,
    decimal BookingPaidAmount,
    decimal BookingRemainingAmount,
    string BookingPaymentStatus,
    string Message);

public sealed class RejectTicketConcessionCommandValidator : AbstractValidator<RejectTicketConcessionCommand>
{
    public RejectTicketConcessionCommandValidator()
    {
        RuleFor(x => x.CodeOrToken).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(300);
    }
}

public sealed class RejectTicketConcessionCommandHandler
    : IRequestHandler<RejectTicketConcessionCommand, RejectTicketConcessionResult>
{
    private const string AdultTicketTypeCode = "ADULT";

    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;
    private readonly IFareCalculator _fareCalculator;
    private readonly TimeProvider _timeProvider;

    public RejectTicketConcessionCommandHandler(
        IApplicationDbContext context,
        IUserContext userContext,
        IFareCalculator fareCalculator,
        TimeProvider timeProvider)
    {
        _context = context;
        _userContext = userContext;
        _fareCalculator = fareCalculator;
        _timeProvider = timeProvider;
    }

    public async Task<RejectTicketConcessionResult> Handle(
        RejectTicketConcessionCommand request,
        CancellationToken cancellationToken)
    {
        var currentUser = await AuthSupport.GetCurrentUserWithRoleAsync(_context, _userContext, cancellationToken);
        if (!AuthSupport.IsAdmin(currentUser)
            && !AuthSupport.IsManager(currentUser)
            && !AuthSupport.IsStaff(currentUser))
        {
            throw new ForbiddenAccessException();
        }

        var now = _timeProvider.GetUtcNow();
        var metadata = request.Metadata ?? new TicketScanRequestMetadata();
        var reason = request.Reason.Trim();
        Ticket? ticket = null;
        TicketStatus? ticketStatusBefore = null;

        try
        {
            ticket = await TicketScanSupport.GetTicketAsync(_context, request.CodeOrToken, cancellationToken);
            ticketStatusBefore = ticket.TicketStatus;
            EnsureTicketCanBeRejected(ticket);
            TicketAttendanceWindowSupport.EnsureCanCheckInAt(ticket, now);
            await TicketStaffScanAuthorizationSupport.EnsureStaffCanOperateTicketAsync(
                _context, currentUser, ticket, now, cancellationToken);
        }
        catch (Exception exception) when (TicketScanHistorySupport.IsLoggableFailure(exception))
        {
            await TicketScanHistorySupport.SaveFailureEventAsync(
                _context,
                currentUser,
                TicketScanAction.ConcessionRejected,
                metadata,
                now,
                request.CodeOrToken,
                ticket,
                ticketStatusBefore,
                exception,
                cancellationToken);
            throw;
        }

        var outcome = await ApplyRejectionAsync(ticket!, currentUser, reason, now, cancellationToken);
        var auditMetadata = metadata with
        {
            Note = BuildAuditNote(metadata.Note, outcome.OriginalPassengerType, outcome.CurrentPassengerType, reason)
        };

        await TicketScanHistorySupport.AddEventAsync(
            _context,
            currentUser,
            TicketScanAction.ConcessionRejected,
            TicketScanResult.Success,
            auditMetadata,
            now,
            request.CodeOrToken,
            ticket!,
            ticketStatusBefore,
            ticket!.TicketStatus,
            null,
            cancellationToken);

        await _context.SaveChangesAsync(cancellationToken);

        var dto = await TicketScanSupport.ToDtoAsync(_context, ticket!, cancellationToken, now);
        return new RejectTicketConcessionResult(
            dto,
            outcome.Action,
            outcome.OriginalPassengerType,
            outcome.CurrentPassengerType,
            outcome.PreviousUnitPrice,
            outcome.CurrentUnitPrice,
            outcome.AdditionalAmount,
            outcome.AdditionalAmount > 0,
            ticket.Booking.TotalAmount,
            PaymentSupport.GetPaidAmount(ticket.Booking),
            ticket.Booking.RemainingAmount,
            ticket.Booking.PaymentStatus,
            outcome.Message);
    }

    private async Task<RejectTicketConcessionOutcome> ApplyRejectionAsync(
        Ticket ticket,
        User actor,
        string reason,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var passenger = ticket.BookingPassenger!;
        var originalPassengerType = TicketTypeCatalog.NormalizeCode(passenger.PassengerType!);
        var previousUnitPrice = passenger.UnitPrice ?? 0m;

        passenger.ReviewedAt = now;
        passenger.ReviewedByUserId = actor.Id;
        passenger.ReviewedByUser = actor;
        passenger.ReviewNote = $"Không đúng đối tượng ưu đãi: {reason}";
        await LoadBookingPaymentsAsync(ticket.Booking, cancellationToken);

        if (IsSightseeingTicket(ticket))
        {
            var adultUnitPrice = await ResolveAdultUnitPriceAsync(ticket, cancellationToken);
            var additionalAmount = Math.Max(adultUnitPrice - previousUnitPrice, 0m);

            passenger.PassengerType = AdultTicketTypeCode;
            passenger.UnitPrice = adultUnitPrice;
            ticket.Booking.SubtotalAmount += additionalAmount;
            ticket.Booking.TotalAmount += additionalAmount;
            PaymentSupport.RestorePaymentSummaryFromPaidPayments(ticket.Booking);

            return new RejectTicketConcessionOutcome(
                "AdjustedToAdult",
                originalPassengerType,
                AdultTicketTypeCode,
                previousUnitPrice,
                adultUnitPrice,
                additionalAmount,
                additionalAmount > 0
                    ? "Vé sightseeing đã đổi sang người lớn. Khách cần thanh toán phần chênh lệch trước khi check-in."
                    : "Vé sightseeing đã đổi sang người lớn. Không phát sinh phần chênh lệch.");
        }

        passenger.ApprovalStatus = "Rejected";
        ticket.TicketStatus = TicketStatus.Cancelled;

        return new RejectTicketConcessionOutcome(
            "Cancelled",
            originalPassengerType,
            originalPassengerType,
            previousUnitPrice,
            previousUnitPrice,
            0m,
            "Vé waterbus thường đã bị hủy do không đúng đối tượng ưu đãi.");
    }

    private async Task<decimal> ResolveAdultUnitPriceAsync(Ticket ticket, CancellationToken cancellationToken)
    {
        var passenger = ticket.BookingPassenger!;
        var trip = passenger.Trip ?? ticket.Booking.Trip;
        if (trip is null)
        {
            throw new ValidationException([new ValidationFailure("trip", "Vé chưa gắn chuyến nên không thể tính chênh lệch.")]);
        }

        if (passenger.TripSeat is null)
        {
            throw new ValidationException([new ValidationFailure("seat", "Vé chưa gắn ghế nên không thể tính chênh lệch.")]);
        }

        return await _fareCalculator.CalculateAsync(
            passenger.TripSeat.SeatId,
            AdultTicketTypeCode,
            cancellationToken,
            trip.Id);
    }

    private async Task LoadBookingPaymentsAsync(Booking booking, CancellationToken cancellationToken)
    {
        booking.Payments = await _context.Set<Payment>()
            .Where(x => x.BookingId == booking.Id)
            .ToListAsync(cancellationToken);
    }

    private static void EnsureTicketCanBeRejected(Ticket ticket)
    {
        if (Booking.IsCharterBookingType(ticket.Booking.BookingType))
        {
            throw new ValidationException([new ValidationFailure("booking",
                "Charter booking không áp dụng kiểm tra loại vé ưu đãi tại endpoint này.")]);
        }

        if (ticket.TicketStatus == TicketStatus.CheckedIn)
        {
            throw new ValidationException([new ValidationFailure("ticket",
                "Vé đã check-in nên không thể reject ưu đãi.")]);
        }

        if (ticket.TicketStatus != TicketStatus.Active)
        {
            throw new ValidationException([new ValidationFailure("ticket",
                "Chỉ có thể reject ưu đãi với vé đang Active.")]);
        }

        if (ticket.Booking.BookingStatus != BookingStatus.Confirmed)
        {
            throw new ValidationException([new ValidationFailure("booking",
                "Booking chưa sẵn sàng để kiểm tra ưu đãi.")]);
        }

        if (ticket.BookingPassenger is null)
        {
            throw new ValidationException([new ValidationFailure("passenger",
                "Vé chưa gắn hành khách nên không thể kiểm tra ưu đãi.")]);
        }

        if (!IsStaffReviewedConcessionType(ticket.BookingPassenger.PassengerType))
        {
            throw new ValidationException([new ValidationFailure("passengerType",
                "Chỉ áp dụng reject ưu đãi cho vé SENIOR hoặc DISABLED.")]);
        }
    }

    private static bool IsSightseeingTicket(Ticket ticket)
    {
        var routeType = ticket.BookingPassenger?.Trip?.Route?.RouteType
            ?? ticket.Booking.Trip?.Route?.RouteType;
        return string.Equals(routeType, RouteTypes.SightseeingLoop, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsStaffReviewedConcessionType(string? passengerType)
    {
        if (string.IsNullOrWhiteSpace(passengerType))
        {
            return false;
        }

        var normalized = TicketTypeCatalog.NormalizeCode(passengerType);
        return normalized is "SENIOR" or "DISABLED";
    }

    private static string BuildAuditNote(
        string? originalNote,
        string originalPassengerType,
        string currentPassengerType,
        string reason)
    {
        var concessionNote =
            $"Concession rejected. Original={originalPassengerType}; Current={currentPassengerType}; Reason={reason}";
        return string.IsNullOrWhiteSpace(originalNote)
            ? concessionNote
            : $"{originalNote.Trim()} | {concessionNote}";
    }

    private sealed record RejectTicketConcessionOutcome(
        string Action,
        string OriginalPassengerType,
        string CurrentPassengerType,
        decimal PreviousUnitPrice,
        decimal CurrentUnitPrice,
        decimal AdditionalAmount,
        string Message);
}
