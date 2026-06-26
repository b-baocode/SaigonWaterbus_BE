using SaigonWaterbus.Application.Common.Interfaces;

namespace SaigonWaterbus.Application.Seats;

public sealed record UpdateSeatStatusRequest(Guid BoatId, Guid SeatId, bool? IsActive);

public sealed class UpdateSeatStatusRequestValidator : AbstractValidator<UpdateSeatStatusRequest>
{
    public UpdateSeatStatusRequestValidator()
    {
        RuleFor(x => x.BoatId)
            .NotEmpty()
            .WithMessage("BoatId không hợp lệ.");

        RuleFor(x => x.SeatId)
            .NotEmpty()
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
            .SingleOrDefaultAsync(x => x.Id == request.SeatId && x.BoatId == request.BoatId, cancellationToken)
            ?? throw new SaigonWaterbus.Application.Common.Exceptions.NotFoundException("Không tìm thấy ghế.");

        seat.IsActive = request.IsActive!.Value;
        await _context.SaveChangesAsync(cancellationToken);

        return SeatSupport.CreateSeatDto(seat);
    }
}
