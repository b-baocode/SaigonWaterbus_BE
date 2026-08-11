using SaigonWaterbus.Application.Common.Interfaces;

namespace SaigonWaterbus.Application.Boats;

public sealed record GetBoatByIdRequest(Guid BoatId);

public sealed class GetBoatByIdRequestValidator : AbstractValidator<GetBoatByIdRequest>
{
    public GetBoatByIdRequestValidator()
    {
        RuleFor(x => x.BoatId)
            .NotEmpty()
            .WithMessage("BoatId không hợp lệ.");
    }
}

public sealed class GetBoatByIdRequestUseCase
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;

    public GetBoatByIdRequestUseCase(
        IApplicationDbContext context,
        IUserContext userContext)
    {
        _context = context;
        _userContext = userContext;
    }

    public async Task<BoatDto> ExecuteAsync(
        GetBoatByIdRequest request,
        CancellationToken cancellationToken)
    {
        // Yêu cầu đăng nhập
        var actor = await BoatSupport.EnsureCurrentUserCanViewBoatsAsync(_context, _userContext, cancellationToken);
        var boat = await BoatSupport.ApplyVisibilityFilter(
                _context.Boats
                    .AsNoTracking()
                    .Include(x => x.Seats)
                    .AsQueryable(),
                actor)
            .SingleOrDefaultAsync(x => x.Id == request.BoatId, cancellationToken)
            ?? throw new SaigonWaterbus.Application.Common.Exceptions.NotFoundException("Không tìm thấy tàu.");

        return BoatSupport.CreateDto(boat);
    }
}
