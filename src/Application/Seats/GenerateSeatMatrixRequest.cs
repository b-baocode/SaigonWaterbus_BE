using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Application.Seats;

public sealed record DeckMatrixConfigDto(
    int DeckNumber,
    int RowCount,
    int ColumnCount);

public sealed record GenerateSeatMatrixRequest(Guid VesselId, IReadOnlyCollection<DeckMatrixConfigDto> Decks);

public sealed class GenerateSeatMatrixRequestValidator : AbstractValidator<GenerateSeatMatrixRequest>
{
    public GenerateSeatMatrixRequestValidator()
    {
        RuleFor(x => x.VesselId)
            .NotEmpty()
            .WithMessage("VesselId không hợp lệ.");

        RuleFor(x => x.Decks)
            .NotEmpty()
            .WithMessage("Cần ít nhất 1 tầng.");

        RuleFor(x => x.Decks)
            .Must(decks => decks.Select(d => d.DeckNumber).Distinct().Count() == decks.Count)
            .WithMessage("Số tầng không được trùng nhau.")
            .When(x => x.Decks is not null);

        RuleForEach(x => x.Decks).ChildRules(deck =>
        {
            deck.RuleFor(d => d.DeckNumber)
                .GreaterThan(0)
                .WithMessage("Số tầng phải lớn hơn 0.");

            deck.RuleFor(d => d.RowCount)
                .InclusiveBetween(1, 26)
                .WithMessage("Số hàng phải từ 1 đến 26.");

            deck.RuleFor(d => d.ColumnCount)
                .InclusiveBetween(1, 50)
                .WithMessage("Số cột phải từ 1 đến 50.");
        }).When(x => x.Decks is not null);
    }
}

public sealed class GenerateSeatMatrixRequestUseCase
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;

    public GenerateSeatMatrixRequestUseCase(IApplicationDbContext context, IUserContext userContext)
    {
        _context = context;
        _userContext = userContext;
    }

    public async Task<VesselSeatsDto> ExecuteAsync(GenerateSeatMatrixRequest request, CancellationToken cancellationToken)
    {
        await SeatSupport.EnsureCurrentUserCanManageSeatsAsync(_context, _userContext, cancellationToken);

        var vessel = await _context.Vessels
            .SingleOrDefaultAsync(x => x.Id == request.VesselId, cancellationToken)
            ?? throw new SaigonWaterbus.Application.Common.Exceptions.NotFoundException("Không tìm thấy tàu.");

        await EnsureVesselHasNoLayoutAsync(vessel.Id, cancellationToken);
        EnsureDecksMatchVessel(vessel, request.Decks);

        var deckLayouts = request.Decks
            .OrderBy(x => x.DeckNumber)
            .Select(deck => new VesselDeckLayout
            {
                VesselId = vessel.Id,
                DeckNumber = deck.DeckNumber,
                RowCount = deck.RowCount,
                ColumnCount = deck.ColumnCount
            })
            .ToArray();

        _context.VesselDeckLayouts.AddRange(deckLayouts);
        vessel.SeatsConfigured = false;
        vessel.Status = VesselStatus.Inactive;
        await _context.SaveChangesAsync(cancellationToken);

        return SeatSupport.CreateVesselSeatsDto(vessel, [], deckLayouts, []);
    }

    private async Task EnsureVesselHasNoLayoutAsync(Guid vesselId, CancellationToken cancellationToken)
    {
        var hasExistingLayout = await _context.Seats.AnyAsync(x => x.VesselId == vesselId, cancellationToken)
            || await _context.VesselDeckLayouts.AnyAsync(x => x.VesselId == vesselId, cancellationToken)
            || await _context.VesselFacilities.AnyAsync(x => x.VesselId == vesselId, cancellationToken)
            || await _context.VesselLayoutCells.AnyAsync(x => x.VesselId == vesselId, cancellationToken);

        if (hasExistingLayout)
            throw AuthSupport.CreateValidationException("Seats", "Tàu đã có ma trận hoặc sơ đồ ghế. Xóa toàn bộ trước khi generate lại.");
    }

    internal static void EnsureDecksMatchVessel(
        Vessel vessel,
        IReadOnlyCollection<DeckMatrixConfigDto> decks)
    {
        if (decks.Count != vessel.NumberOfDecks)
            throw AuthSupport.CreateValidationException(
                "Decks",
                $"Số tầng cấu hình ({decks.Count}) không khớp với NumberOfDecks của tàu ({vessel.NumberOfDecks}).");

        var deckNumbers = decks
            .Select(d => d.DeckNumber)
            .OrderBy(deckNumber => deckNumber)
            .ToArray();
        var expectedDeckNumbers = Enumerable.Range(1, vessel.NumberOfDecks).ToArray();

        if (!deckNumbers.SequenceEqual(expectedDeckNumbers))
            throw AuthSupport.CreateValidationException(
                "Decks",
                $"Số tầng cấu hình phải liên tục từ 1 đến {vessel.NumberOfDecks}.");
    }
}
