using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Interfaces;

namespace SaigonWaterbus.Application.Seats;

public sealed record DeleteAllSeatsRequest(int VesselId);

public sealed class DeleteAllSeatsRequestValidator : AbstractValidator<DeleteAllSeatsRequest>
{
    public DeleteAllSeatsRequestValidator()
    {
        RuleFor(x => x.VesselId)
            .GreaterThan(0)
            .WithMessage("VesselId không hợp lệ.");
    }
}

public sealed class DeleteAllSeatsRequestUseCase
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;

    public DeleteAllSeatsRequestUseCase(IApplicationDbContext context, IUserContext userContext)
    {
        _context = context;
        _userContext = userContext;
    }

    public async Task<AuthActionResultDto> ExecuteAsync(DeleteAllSeatsRequest request, CancellationToken cancellationToken)
    {
        await SeatSupport.EnsureCurrentUserCanManageSeatsAsync(_context, _userContext, cancellationToken);

        var vessel = await _context.Vessels
            .SingleOrDefaultAsync(x => x.Id == request.VesselId, cancellationToken)
            ?? throw new SaigonWaterbus.Application.Common.Exceptions.NotFoundException("Không tìm thấy tàu.");

        var seats = await _context.Seats
            .Where(x => x.VesselId == vessel.Id)
            .ToListAsync(cancellationToken);
        var deckLayouts = await _context.VesselDeckLayouts
            .Where(x => x.VesselId == vessel.Id)
            .ToListAsync(cancellationToken);
        var facilities = await _context.VesselFacilities
            .Where(x => x.VesselId == vessel.Id)
            .ToListAsync(cancellationToken);

        if (seats.Count == 0 && deckLayouts.Count == 0 && facilities.Count == 0)
            throw AuthSupport.CreateValidationException("Seats", "Tàu chưa có sơ đồ ghế nào.");

        _context.Seats.RemoveRange(seats);
        _context.VesselDeckLayouts.RemoveRange(deckLayouts);
        _context.VesselFacilities.RemoveRange(facilities);
        vessel.SeatsConfigured = false;
        await _context.SaveChangesAsync(cancellationToken);

        return new AuthActionResultDto("Xóa toàn bộ sơ đồ ghế thành công.");
    }
}
