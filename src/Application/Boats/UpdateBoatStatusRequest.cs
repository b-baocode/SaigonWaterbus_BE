using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Application.Boats;

public sealed record UpdateBoatStatusRequest(
    Guid BoatId,
    BoatStatus Status);

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
    }
}

public sealed class UpdateBoatStatusRequestUseCase
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;
    private readonly TimeProvider _timeProvider;

    public UpdateBoatStatusRequestUseCase(
        IApplicationDbContext context,
        IUserContext userContext,
        TimeProvider timeProvider)
    {
        _context = context;
        _userContext = userContext;
        _timeProvider = timeProvider;
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
        await _context.SaveChangesAsync(cancellationToken);

        return BoatSupport.CreateDto(boat);
    }
}
