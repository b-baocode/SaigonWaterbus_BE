using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Application.Notifications;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Application.Boats;

public sealed record UpdateBoatStatusRequest(
    Guid BoatId,
    BoatStatus Status,
    DateTimeOffset? EstimatedMaintenanceEndAt = null,
    string? MaintenanceNote = null);

public sealed class UpdateBoatStatusRequestValidator : AbstractValidator<UpdateBoatStatusRequest>
{
    public UpdateBoatStatusRequestValidator()
    {
        RuleFor(x => x.BoatId)
            .NotEmpty()
            .WithMessage("BoatId không hợp lệ.");

        RuleFor(x => x.Status)
            .IsInEnum()
            .WithMessage("Trạng thái tàu không hợp lệ.");

        RuleFor(x => x.MaintenanceNote)
            .MaximumLength(1000)
            .WithMessage("Ghi chú bảo trì không được vượt quá 1000 ký tự.");
    }
}

public sealed class UpdateBoatStatusRequestUseCase
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;
    private readonly TimeProvider _timeProvider;
    private readonly INotificationRealtimeNotifier? _notificationRealtimeNotifier;

    public UpdateBoatStatusRequestUseCase(
        IApplicationDbContext context,
        IUserContext userContext,
        TimeProvider timeProvider,
        INotificationRealtimeNotifier? notificationRealtimeNotifier = null)
    {
        _context = context;
        _userContext = userContext;
        _timeProvider = timeProvider;
        _notificationRealtimeNotifier = notificationRealtimeNotifier;
    }

    public async Task<BoatDto> ExecuteAsync(
        UpdateBoatStatusRequest request,
        CancellationToken cancellationToken)
    {
        await BoatSupport.EnsureCurrentUserCanManageBoatsAsync(_context, _userContext, cancellationToken);

        var boat = await _context.Boats
            .Include(x => x.Seats)
            .SingleOrDefaultAsync(x => x.Id == request.BoatId, cancellationToken)
            ?? throw new SaigonWaterbus.Application.Common.Exceptions.NotFoundException("Không tìm thấy tàu.");

        var previousStatus = boat.Status;

        if (request.Status == BoatStatus.Active)
        {
            if (boat.ServiceType == BoatServiceType.Passenger)
            {
                var configuredSeats = boat.Seats.Count;
                if (boat.SeatCount <= 0 || configuredSeats != boat.SeatCount)
                {
                    throw SaigonWaterbus.Application.Auth.Common.AuthSupport.CreateValidationException(
                        nameof(request.Status),
                        $"Tàu cần cấu hình đủ {boat.SeatCount} ghế trước khi chuyển Active. Hiện có {configuredSeats} ghế.");
                }

                BoatDocumentSupport.EnsureCanActivate(boat);
            }

            BoatSupport.EnsureCanActivate(boat, nameof(request.Status));
        }

        if (request.Status == BoatStatus.UnderMaintenance
            && boat.Status != BoatStatus.UnderMaintenance)
        {
            boat.MaintenanceStartedAt = _timeProvider.GetUtcNow();
        }

        boat.Status = request.Status;
        if (request.Status == BoatStatus.UnderMaintenance)
        {
            boat.EstimatedMaintenanceEndAt = request.EstimatedMaintenanceEndAt?.ToUniversalTime();
            boat.MaintenanceNote = AuthSupport.NormalizeOptionalText(request.MaintenanceNote);
        }
        else
        {
            boat.EstimatedMaintenanceEndAt = null;
            boat.MaintenanceNote = null;
        }

        await _context.SaveChangesAsync(cancellationToken);

        // Tàu vừa vào bảo trì → quét các charter booking Quoted/Confirmed còn departureDate tương lai
        // đang dùng tàu này (qua BoatId hoặc CharterBoats) để báo admin/manager (đổi tàu) + customer (yên tâm).
        var now = _timeProvider.GetUtcNow();
        if (previousStatus != BoatStatus.UnderMaintenance && boat.Status == BoatStatus.UnderMaintenance)
        {
            var notifications = await NotificationSupport.AddCharterBoatMaintenanceAffectsBookingNotificationsAsync(
                _context,
                boat,
                boat.EstimatedMaintenanceEndAt,
                boat.MaintenanceNote,
                now,
                cancellationToken);

            if (notifications.Count > 0)
            {
                await _context.SaveChangesAsync(cancellationToken);
                await NotificationSupport.PublishCreatedAsync(
                    _notificationRealtimeNotifier,
                    notifications,
                    cancellationToken);
            }
        }

        return BoatSupport.CreateDto(boat);
    }
}
