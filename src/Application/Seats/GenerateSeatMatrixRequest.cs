using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Application.Seats;

public sealed record DeckMatrixConfigDto(
    int DeckNumber,
    int RowCount,
    int ColumnCount);

public sealed record GenerateSeatMatrixRequest(Guid BoatId, IReadOnlyCollection<DeckMatrixConfigDto> Decks);

public sealed class GenerateSeatMatrixRequestValidator : AbstractValidator<GenerateSeatMatrixRequest>
{
    public GenerateSeatMatrixRequestValidator()
    {
        RuleFor(x => x.BoatId)
            .NotEmpty()
            .WithMessage("BoatId không hợp lệ.");

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

    public async Task<BoatSeatsDto> ExecuteAsync(GenerateSeatMatrixRequest request, CancellationToken cancellationToken)
    {
        await SeatSupport.EnsureCurrentUserCanManageSeatsAsync(_context, _userContext, cancellationToken);

        var boat = await _context.Boats
            .SingleOrDefaultAsync(x => x.Id == request.BoatId, cancellationToken)
            ?? throw new SaigonWaterbus.Application.Common.Exceptions.NotFoundException("Không tìm thấy tàu.");

        await EnsureBoatHasNoLayoutAsync(boat.Id, cancellationToken);
        EnsureDecksMatchBoat(boat, request.Decks);
        var deckLayouts = request.Decks
            .OrderBy(x => x.DeckNumber)
            .Select(deck => new SeatDeckLayout(deck.DeckNumber, deck.RowCount, deck.ColumnCount))
            .ToArray();

        boat.SeatsConfigured = false;
        boat.Status = BoatStatus.Inactive;

        return SeatSupport.CreateBoatSeatsDto(
            boat,
            [],
            deckLayouts);
    }

    private async Task EnsureBoatHasNoLayoutAsync(Guid boatId, CancellationToken cancellationToken)
    {
        var hasExistingLayout = await _context.Seats.AnyAsync(x => x.BoatId == boatId, cancellationToken);

        if (hasExistingLayout)
            throw AuthSupport.CreateValidationException("Seats", "Tàu đã có sơ đồ ghế. Xóa toàn bộ trước khi generate lại.");
    }

    internal static void EnsureDecksMatchBoat(
        Boat boat,
        IReadOnlyCollection<DeckMatrixConfigDto> decks)
    {
        if (decks.Count != boat.NumberOfDecks)
            throw AuthSupport.CreateValidationException(
                "Decks",
                $"Số tầng cấu hình ({decks.Count}) không khớp với NumberOfDecks của tàu ({boat.NumberOfDecks}).");

        var deckNumbers = decks
            .Select(d => d.DeckNumber)
            .OrderBy(deckNumber => deckNumber)
            .ToArray();
        var expectedDeckNumbers = Enumerable.Range(1, boat.NumberOfDecks).ToArray();

        if (!deckNumbers.SequenceEqual(expectedDeckNumbers))
            throw AuthSupport.CreateValidationException(
                "Decks",
                $"Số tầng cấu hình phải liên tục từ 1 đến {boat.NumberOfDecks}.");
    }
}
