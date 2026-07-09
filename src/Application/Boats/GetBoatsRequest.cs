using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Application.Boats;

public sealed record GetBoatsRequest(
    BoatStatus? Status = null,
    string? Search = null);

public sealed record BoatRentalPriceDto(
    BoatRentalUnit RentalUnit,
    decimal UnitPrice,
    string Currency,
    string? Note);

public sealed record BoatRentalPriceRequest(
    BoatRentalUnit RentalUnit,
    decimal UnitPrice,
    string? Currency = null,
    string? Note = null);

public sealed record BoatImageFileRequest(
    string FileName,
    string? ContentType,
    long Length,
    Stream Content);

public sealed record BoatDto(
    Guid Id,
    string Code,
    string? RegistrationNumber,
    string Name,
    BoatStatus Status,
    int SeatCount,
    int NumberOfDecks,
    bool SeatsConfigured,
    bool IsReadyForOperation,
    int? MaxSpeedKmh,
    int? YearBuilt,
    string ImageUrl,
    IReadOnlyCollection<string> ImageUrls,
    string? Description,
    IReadOnlyCollection<BoatRentalPriceDto> RentalPrices,
    SeatSetupType SeatSetupType,
    DateTimeOffset? MaintenanceStartedAt,
    bool DocumentsRequireRefresh);

public sealed class GetBoatsRequestValidator : AbstractValidator<GetBoatsRequest>
{
    public GetBoatsRequestValidator()
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

public sealed class GetBoatsRequestUseCase
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;

    public GetBoatsRequestUseCase(
        IApplicationDbContext context,
        IUserContext userContext)
    {
        _context = context;
        _userContext = userContext;
    }

    public async Task<IReadOnlyCollection<BoatDto>> ExecuteAsync(
        GetBoatsRequest request,
        CancellationToken cancellationToken)
    {
        var actor = await BoatSupport.EnsureCurrentUserCanViewBoatsAsync(_context, _userContext, cancellationToken);
        var query = BoatSupport.ApplyVisibilityFilter(
            _context.Boats
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
            var searchById = Guid.TryParse(rawKeyword, out var boatId);
            query = query.Where(x =>
                (searchById && x.Id == boatId)
                || x.Code.Contains(keyword)
                || x.Name.ToUpper().Contains(keyword)
                || (x.RegistrationNumber != null && x.RegistrationNumber.Contains(keyword)));
        }

        var boats = await query
            .OrderBy(x => x.Code)
            .ThenBy(x => x.Id)
            .ToListAsync(cancellationToken);

        return boats.Select(BoatSupport.CreateDto).ToArray();
    }
}
