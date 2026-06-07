using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Application.Vessels;

public sealed record GetVesselsRequest(
    int? ServiceId = null,
    VesselStatus? Status = null,
    string? Search = null);

public sealed record VesselWaterbusServiceDto(
    int Id,
    string Code,
    string Name);

public sealed record VesselDto(
    int Id,
    VesselWaterbusServiceDto WaterbusService,
    string Code,
    string? RegistrationNumber,
    string Name,
    VesselStatus Status,
    int SeatCount,
    int GeneratedSeatCount,
    int NumberOfDecks,
    bool SeatsConfigured,
    int? MaxSpeedKmh,
    int? YearBuilt,
    string ImageUrl,
    string? Description);

public sealed class GetVesselsRequestValidator : AbstractValidator<GetVesselsRequest>
{
    public GetVesselsRequestValidator()
    {
        RuleFor(x => x.ServiceId)
            .GreaterThan(0)
            .WithMessage("ServiceId không hợp lệ.")
            .When(x => x.ServiceId.HasValue);

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
                .Include(x => x.WaterbusService)
                .AsQueryable(),
            actor);

        if (request.ServiceId.HasValue)
        {
            query = query.Where(x => x.WaterbusServiceId == request.ServiceId.Value);
        }

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
