using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Interfaces;

namespace SaigonWaterbus.Application.Seats;

public sealed record UpdateSeatRequest(Guid VesselId, Guid SeatId, string? Code);

public sealed class UpdateSeatRequestValidator : AbstractValidator<UpdateSeatRequest>
{
    public UpdateSeatRequestValidator()
    {
        RuleFor(x => x.VesselId)
            .NotEmpty()
            .WithMessage("VesselId không hợp lệ.");

        RuleFor(x => x.SeatId)
            .NotEmpty()
            .WithMessage("SeatId không hợp lệ.");

        RuleFor(x => x.Code)
            .NotEmpty()
            .WithMessage("Mã ghế không được để trống.")
            .MaximumLength(20)
            .WithMessage("Mã ghế không được vượt quá 20 ký tự.")
            .When(x => x.Code is not null);
    }
}

public sealed class UpdateSeatRequestUseCase
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;

    public UpdateSeatRequestUseCase(IApplicationDbContext context, IUserContext userContext)
    {
        _context = context;
        _userContext = userContext;
    }

    public async Task<SeatDto> ExecuteAsync(UpdateSeatRequest request, CancellationToken cancellationToken)
    {
        await SeatSupport.EnsureCurrentUserCanManageSeatsAsync(_context, _userContext, cancellationToken);

        var seat = await _context.Seats
            .SingleOrDefaultAsync(x => x.Id == request.SeatId && x.VesselId == request.VesselId, cancellationToken)
            ?? throw new SaigonWaterbus.Application.Common.Exceptions.NotFoundException("Không tìm thấy ghế.");

        if (request.Code is not null)
        {
            var normalizedCode = request.Code.Trim().ToUpperInvariant();
            var codeExists = await _context.Seats
                .AnyAsync(x => x.VesselId == request.VesselId && x.Code == normalizedCode && x.Id != request.SeatId, cancellationToken);
            if (codeExists)
                throw AuthSupport.CreateValidationException(nameof(request.Code), "Mã ghế đã tồn tại trên tàu này.");

            seat.Code = normalizedCode;
        }

        await _context.SaveChangesAsync(cancellationToken);
        return SeatSupport.CreateSeatDto(seat);
    }
}
