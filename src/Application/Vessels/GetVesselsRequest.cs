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

public sealed record VesselRentalPriceRequest(
    VesselRentalUnit RentalUnit,
    decimal UnitPrice,
    string? Currency = null,
    string? Note = null);

public sealed record VesselImageFileRequest(
    string FileName,
    string? ContentType,
    long Length,
    Stream Content);

public sealed record VesselDto(
    Guid Id,
    string Code,
    string? RegistrationNumber,
    string Name,
    VesselStatus Status,
    int SeatCount,
    int NumberOfDecks,
    bool SeatsConfigured,
    bool IsReadyForOperation,
    int? MaxSpeedKmh,
    int? YearBuilt,
    string ImageUrl,
    IReadOnlyCollection<string> ImageUrls,
    string? Description,
    IReadOnlyCollection<VesselRentalPriceDto> RentalPrices,
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
                .Include(x => x.Seats)
                .AsQueryable(),
            actor);

        if (request.Status.HasValue)
        {
            query = query.Where(x => x.Status == request.Status.Value);
        }

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var rawKeyword = request.Search.Trim();
            var keyword = rawKeyword.ToUpperInvariant();
            var searchById = Guid.TryParse(rawKeyword, out var vesselId);
            query = query.Where(x =>
                (searchById && x.Id == vesselId)
                || x.Code.Contains(keyword)
                || x.Name.ToUpper().Contains(keyword)
                || (x.RegistrationNumber != null && x.RegistrationNumber.Contains(keyword)));
        }

        var vessels = await query
            .OrderBy(x => x.Code)
            .ThenBy(x => x.Id)
            .ToListAsync(cancellationToken);

        return vessels.Select(VesselSupport.CreateDto).ToArray();
    }
}
