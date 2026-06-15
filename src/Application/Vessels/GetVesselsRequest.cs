using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Application.Vessels;

public sealed record GetVesselsRequest(
    VesselStatus? Status = null,
    string? Search = null);

public sealed record VesselRentalPriceDto(
    VesselRentalUnit RentalUnit,
    decimal UnitPrice,
    string Currency,
    string? Note);

public sealed record VesselDto(
    Guid Id,
    string Code,
    string? RegistrationNumber,
    string Name,
    VesselStatus Status,
    int SeatCount,
    int PassengerCapacity,
    int GeneratedSeatCount,
    int NumberOfDecks,
    bool SeatsConfigured,
    bool IsReadyForOperation,
    int? MaxSpeedKmh,
    int? YearBuilt,
    string ImageUrl,
    string? Description,
    VesselRentalPriceDto? RentalPrice,
    SeatSetupType SeatSetupType);

public sealed class GetVesselsRequestValidator : AbstractValidator<GetVesselsRequest>
{
    public GetVesselsRequestValidator()
    {
        RuleFor(x => x.Status)
            .IsInEnum()
            .WithMessage("Trạng thái tàu không hợp lệ.")
            .When(x => x.Status.HasValue);

        RuleFor(x => x.Search)
            .MaximumLength(100)
            .WithMessage("Từ khóa tìm kiếm không được vượt quá 100 ký tự.");
    }
}

public sealed class GetVesselsRequestUseCase
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;

    public GetVesselsRequestUseCase(
        IApplicationDbContext context,
        IUserContext userContext)
    {
        _context = context;
        _userContext = userContext;
    }

    public async Task<IReadOnlyCollection<VesselDto>> ExecuteAsync(
        GetVesselsRequest request,
        CancellationToken cancellationToken)
    {
        var actor = await VesselSupport.EnsureCurrentUserCanViewVesselsAsync(_context, _userContext, cancellationToken);
        var query = VesselSupport.ApplyVisibilityFilter(
            _context.Vessels
                .AsNoTracking()
                .Include(x => x.RentalPrices)
                .AsQueryable(),
            actor);

        if (request.Status.HasValue)
        {
            query = query.Where(x => x.Status == request.Status.Value);
        }

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var keyword = request.Search.Trim().ToUpperInvariant();
            query = query.Where(x =>
                x.Code.Contains(keyword)
                || x.Name.ToUpper().Contains(keyword)
                || (x.RegistrationNumber != null && x.RegistrationNumber.Contains(keyword)));
        }

        var vessels = await query
            .OrderBy(x => x.Code)
            .ThenBy(x => x.Id)
            .ToListAsync(cancellationToken);

        var vesselIds = vessels.Select(v => v.Id).ToList();
        var seatCounts = await _context.Seats
            .AsNoTracking()
            .Where(s => vesselIds.Contains(s.VesselId))
            .GroupBy(s => s.VesselId)
            .Select(g => new { VesselId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.VesselId, x => x.Count, cancellationToken);

        return vessels.Select(v => VesselSupport.CreateDto(v, seatCounts.GetValueOrDefault(v.Id, 0))).ToArray();
    }
}
