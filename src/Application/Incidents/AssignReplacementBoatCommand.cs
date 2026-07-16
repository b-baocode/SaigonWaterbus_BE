using FluentValidation.Results;
using SaigonWaterbus.Application.Boats;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.Incidents;

public sealed record AssignReplacementBoatCommand(
    Guid IncidentId,
    Guid ReplacementBoatId,
    int? DelayMinutes,
    string? Note) : IRequest<IncidentDto>;

public sealed class AssignReplacementBoatCommandValidator : AbstractValidator<AssignReplacementBoatCommand>
{
    public AssignReplacementBoatCommandValidator()
    {
        RuleFor(x => x.IncidentId).NotEmpty();
        RuleFor(x => x.ReplacementBoatId).NotEmpty();
        RuleFor(x => x.DelayMinutes).GreaterThanOrEqualTo(0).When(x => x.DelayMinutes.HasValue);
        RuleFor(x => x.Note).MaximumLength(1000);
    }
}

public sealed class AssignReplacementBoatCommandHandler : IRequestHandler<AssignReplacementBoatCommand, IncidentDto>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;
    private readonly TimeProvider _timeProvider;
    private readonly IIncidentRealtimeNotifier _realtimeNotifier;

    public AssignReplacementBoatCommandHandler(
        IApplicationDbContext context,
        IUserContext userContext,
        TimeProvider timeProvider,
        IIncidentRealtimeNotifier? realtimeNotifier = null)
    {
        _context = context;
        _userContext = userContext;
        _timeProvider = timeProvider;
        _realtimeNotifier = realtimeNotifier ?? NullIncidentRealtimeNotifier.Instance;
    }

    public async Task<IncidentDto> Handle(
        AssignReplacementBoatCommand request,
        CancellationToken cancellationToken)
    {
        var actor = await IncidentSupport.EnsureCurrentUserCanResolveIncidentAsync(
            _context,
            _userContext,
            cancellationToken);

        var incident = await LoadIncidentQuery()
            .SingleOrDefaultAsync(x => x.Id == request.IncidentId, cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy sự cố.");

        if (incident.BoatId == request.ReplacementBoatId)
        {
            throw new ValidationException([new ValidationFailure(
                nameof(request.ReplacementBoatId),
                "Tàu thay thế không được trùng với tàu gặp sự cố.")]);
        }

        var replacementBoat = await _context.Boats
            .Include(x => x.Seats)
            .SingleOrDefaultAsync(x => x.Id == request.ReplacementBoatId, cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy tàu thay thế.");

        if (replacementBoat.Status != BoatStatus.Active || !BoatSupport.IsReadyForOperation(replacementBoat))
        {
            throw new ValidationException([new ValidationFailure(
                nameof(request.ReplacementBoatId),
                "Tàu thay thế phải Active và đã setup đủ ghế.")]);
        }

        if (incident.Trip is not null)
        {
            var activeTicketCount = await CountActiveTicketsAsync(incident.Trip.Id, cancellationToken);
            if (replacementBoat.SeatCount < activeTicketCount)
            {
                throw new ValidationException([new ValidationFailure(
                    nameof(request.ReplacementBoatId),
                    $"Tàu thay thế không đủ ghế. Cần tối thiểu {activeTicketCount} ghế.")]);
            }
        }

        incident.ReplacementBoatId = replacementBoat.Id;
        incident.ReplacementBoat = replacementBoat;
        incident.ReplacementAssignedAt = _timeProvider.GetUtcNow();
        incident.ReplacementAssignedByUserId = actor.Id;
        incident.ReplacementAssignedByUser = actor;
        incident.ReplacementNote = NormalizeNote(request.Note);

        if (incident.Trip is not null)
        {
            incident.Trip.BoatId = replacementBoat.Id;
            incident.Trip.Boat = replacementBoat;
            if (incident.Trip.TripStatus is not TripStatus.Completed and not TripStatus.Cancelled)
            {
                incident.Trip.TripStatus = request.DelayMinutes.GetValueOrDefault() > 0
                    ? TripStatus.Delayed
                    : TripStatus.Scheduled;
            }

            incident.Trip.StatusNote = incident.ReplacementNote
                ?? $"Đã điều tàu {replacementBoat.Name} thay thế.";
        }

        await _context.SaveChangesAsync(cancellationToken);

        incident = await LoadIncidentQuery().SingleAsync(x => x.Id == request.IncidentId, cancellationToken);
        await _realtimeNotifier.PublishChangedAsync(
            IncidentSupport.ToRealtimeEvent(incident, "RescueDispatched", incident.ReplacementAssignedAt),
            cancellationToken);
        return IncidentSupport.ToDto(incident);
    }

    private async Task<int> CountActiveTicketsAsync(Guid tripId, CancellationToken cancellationToken) =>
        // Vé thuộc trip theo chiều của hành khách (booking khứ hồi có vé trên 2 trip);
        // vé không gắn hành khách hoặc chưa có TripId (dữ liệu cũ/charter) tính theo trip của booking.
        await _context.Tickets.CountAsync(
            x => (x.BookingPassenger != null && x.BookingPassenger.TripId != null
                    ? x.BookingPassenger.TripId == tripId
                    : x.Booking.TripId == tripId)
              && x.TicketStatus != TicketStatus.Cancelled
              && x.TicketStatus != TicketStatus.Expired,
            cancellationToken);

    private static string? NormalizeNote(string? note) =>
        string.IsNullOrWhiteSpace(note) ? null : note.Trim();

    private IQueryable<Incident> LoadIncidentQuery() =>
        _context.Incidents
            .Include(x => x.Boat)
            .Include(x => x.Trip)
            .Include(x => x.Reporter)
            .Include(x => x.AssignedManager)
            .Include(x => x.AssignedByUser)
            .Include(x => x.ReplacementBoat)
            .Include(x => x.ReplacementAssignedByUser)
            .Include(x => x.Resolver);
}
