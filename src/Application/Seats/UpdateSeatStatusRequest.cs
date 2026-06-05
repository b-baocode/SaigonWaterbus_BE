using SaigonWaterbus.Application.Common.Interfaces;

namespace SaigonWaterbus.Application.Seats;

public sealed record UpdateSeatStatusRequest(int VesselId, int SeatId, bool? IsActive);

public sealed class UpdateSeatStatusRequestValidator : AbstractValidator<UpdateSeatStatusRequest>
{
    public UpdateSeatStatusRequestValidator()
    {
        RuleFor(x => x.VesselId)
            .GreaterThan(0)
            .WithMessage("VesselId không hợp lệ.");

        RuleFor(x => x.SeatId)
            .GreaterThan(0)
            .WithMessage("SeatId không hợp lệ.");

        RuleFor(x => x.IsActive)
            .NotNull()
            .WithMessage("Trạng thái ghế là bắt buộc.");
    }
}

public sealed class UpdateSeatStatusRequestUseCase
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;

    public UpdateSeatStatusRequestUseCase(IApplicationDbContext context, IUserContext userContext)
    {
        _context = context;
        _userContext = userContext;
    }

    public async Task<SeatDto> ExecuteAsync(UpdateSeatStatusRequest request, CancellationToken cancellationToken)
    {
        await SeatSupport.EnsureCurrentUserCanManageSeatsAsync(_context, _userContext, cancellationToken);

        var seat = await _context.Seats
            .SingleOrDefaultAsync(x => x.Id == request.SeatId && x.VesselId == request.VesselId, cancellationToken)
            ?? throw new SaigonWaterbus.Application.Common.Exceptions.NotFoundException("Không tìm thấy ghế.");

        if (request.IsActive is null)
            throw SaigonWaterbus.Application.Auth.Common.AuthSupport.CreateValidationException(
                nameof(request.IsActive),
                "Trạng thái ghế là bắt buộc.");

        seat.IsActive = request.IsActive.Value;
        await _context.SaveChangesAsync(cancellationToken);

        return new SeatDto(seat.Id, seat.Code, seat.Deck, seat.Row, seat.Column, seat.IsActive);
    }
}
