using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Interfaces;

namespace SaigonWaterbus.Application.Boats;

public sealed record DeleteBoatRequest(Guid BoatId);

public sealed class DeleteBoatRequestValidator : AbstractValidator<DeleteBoatRequest>
{
    public DeleteBoatRequestValidator()
    {
        RuleFor(x => x.BoatId)
            .NotEmpty()
            .WithMessage("BoatId không hợp lệ.");
    }
}

public sealed class DeleteBoatRequestUseCase
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;

    public DeleteBoatRequestUseCase(
        IApplicationDbContext context,
        IUserContext userContext)
    {
        _context = context;
        _userContext = userContext;
    }

    public async Task<AuthActionResultDto> ExecuteAsync(
        DeleteBoatRequest request,
        CancellationToken cancellationToken)
    {
        await BoatSupport.EnsureCurrentUserCanManageBoatsAsync(_context, _userContext, cancellationToken);

        var boat = await _context.Boats
            .SingleOrDefaultAsync(x => x.Id == request.BoatId, cancellationToken)
            ?? throw new SaigonWaterbus.Application.Common.Exceptions.NotFoundException("Không tìm thấy tàu.");

        _context.Boats.Remove(boat);
        await _context.SaveChangesAsync(cancellationToken);

        return new AuthActionResultDto("Xóa tàu thành công.");
    }
}
