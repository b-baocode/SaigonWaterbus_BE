using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Application.Seats;

public sealed record DeleteAllSeatsRequest(Guid BoatId);

public sealed class DeleteAllSeatsRequestValidator : AbstractValidator<DeleteAllSeatsRequest>
{
    public DeleteAllSeatsRequestValidator()
    {
        RuleFor(x => x.BoatId)
            .NotEmpty()
            .WithMessage("BoatId không hợp lệ.");
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

        var boat = await _context.Boats
            .SingleOrDefaultAsync(x => x.Id == request.BoatId, cancellationToken)
            ?? throw new SaigonWaterbus.Application.Common.Exceptions.NotFoundException("Không tìm thấy tàu.");

        var hasAny = await _context.Seats.AnyAsync(x => x.BoatId == boat.Id, cancellationToken);

        if (!hasAny)
            throw AuthSupport.CreateValidationException("Seats", "Tàu chưa có ghế nào.");

        await _context.Seats.Where(x => x.BoatId == boat.Id).ExecuteDeleteAsync(cancellationToken);
        boat.SeatsConfigured = false;
        boat.Status = BoatStatus.Inactive;
        await _context.SaveChangesAsync(cancellationToken);

        return new AuthActionResultDto("Xóa toàn bộ sơ đồ ghế thành công.");
    }
}
