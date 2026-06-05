using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Interfaces;

namespace SaigonWaterbus.Application.Seats;

public sealed record DeleteSeatRequest(int VesselId, int SeatId);

public sealed class DeleteSeatRequestValidator : AbstractValidator<DeleteSeatRequest>
{
    public DeleteSeatRequestValidator()
    {
        RuleFor(x => x.VesselId)
            .GreaterThan(0)
            .WithMessage("VesselId không hợp lệ.");

        RuleFor(x => x.SeatId)
            .GreaterThan(0)
            .WithMessage("SeatId không hợp lệ.");
    }
}

public sealed class DeleteSeatRequestUseCase
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;

    public DeleteSeatRequestUseCase(IApplicationDbContext context, IUserContext userContext)
    {
        _context = context;
        _userContext = userContext;
    }

    public async Task<AuthActionResultDto> ExecuteAsync(DeleteSeatRequest request, CancellationToken cancellationToken)
    {
        await SeatSupport.EnsureCurrentUserCanManageSeatsAsync(_context, _userContext, cancellationToken);

        var vessel = await _context.Vessels
            .SingleOrDefaultAsync(x => x.Id == request.VesselId, cancellationToken)
            ?? throw new SaigonWaterbus.Application.Common.Exceptions.NotFoundException("Không tìm thấy tàu.");

        var seat = await _context.Seats
            .SingleOrDefaultAsync(x => x.Id == request.SeatId && x.VesselId == vessel.Id, cancellationToken)
            ?? throw new SaigonWaterbus.Application.Common.Exceptions.NotFoundException("Không tìm thấy ghế.");

        _context.Seats.Remove(seat);
        vessel.SeatsConfigured = false;
        await _context.SaveChangesAsync(cancellationToken);

        return new AuthActionResultDto("Xóa ghế thành công.");
    }
}
