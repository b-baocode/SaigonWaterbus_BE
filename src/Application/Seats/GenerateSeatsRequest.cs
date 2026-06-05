using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Application.Seats;

public sealed record DeckConfigDto(int DeckNumber, int RowCount, int ColumnCount);

public sealed record GenerateSeatsRequest(int VesselId, IReadOnlyCollection<DeckConfigDto> Decks);

public sealed class GenerateSeatsRequestValidator : AbstractValidator<GenerateSeatsRequest>
{
    public GenerateSeatsRequestValidator()
    {
        RuleFor(x => x.VesselId)
            .GreaterThan(0)
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

public sealed class GenerateSeatsRequestUseCase
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;

    public GenerateSeatsRequestUseCase(IApplicationDbContext context, IUserContext userContext)
    {
        _context = context;
        _userContext = userContext;
    }

    public async Task<VesselSeatsDto> ExecuteAsync(GenerateSeatsRequest request, CancellationToken cancellationToken)
    {
        await SeatSupport.EnsureCurrentUserCanManageSeatsAsync(_context, _userContext, cancellationToken);

        var vessel = await _context.Vessels
            .SingleOrDefaultAsync(x => x.Id == request.VesselId, cancellationToken)
            ?? throw new SaigonWaterbus.Application.Common.Exceptions.NotFoundException("Không tìm thấy tàu.");

        var hasExistingSeats = await _context.Seats
            .AnyAsync(x => x.VesselId == request.VesselId, cancellationToken);

        if (hasExistingSeats)
            throw AuthSupport.CreateValidationException("Seats", "Tàu đã có ghế. Xóa toàn bộ ghế trước khi generate lại.");

        if (request.Decks.Count != vessel.NumberOfDecks)
            throw AuthSupport.CreateValidationException(
                "Decks",
                $"Số tầng cấu hình ({request.Decks.Count}) không khớp với NumberOfDecks của tàu ({vessel.NumberOfDecks}).");

        var deckNumbers = request.Decks
            .Select(d => d.DeckNumber)
            .OrderBy(deckNumber => deckNumber)
            .ToArray();
        var expectedDeckNumbers = Enumerable.Range(1, vessel.NumberOfDecks).ToArray();
        if (!deckNumbers.SequenceEqual(expectedDeckNumbers))
            throw AuthSupport.CreateValidationException(
                "Decks",
                $"Số tầng cấu hình phải liên tục từ 1 đến {vessel.NumberOfDecks}.");

        var totalGenerated = request.Decks.Sum(d => d.RowCount * d.ColumnCount);
        if (totalGenerated != vessel.SeatCount)
            throw AuthSupport.CreateValidationException(
                "Decks",
                $"Tổng số ghế generate ({totalGenerated}) không khớp với SeatCount của tàu ({vessel.SeatCount}).");

        var seats = new List<Seat>();
        foreach (var deck in request.Decks.OrderBy(d => d.DeckNumber))
        {
            for (var r = 0; r < deck.RowCount; r++)
            {
                var row = SeatSupport.RowLabel(r);
                for (var col = 1; col <= deck.ColumnCount; col++)
                {
                    seats.Add(new Seat
                    {
                        VesselId = vessel.Id,
                        Code = SeatSupport.SeatCode(deck.DeckNumber, row, col),
                        Deck = deck.DeckNumber,
                        Row = row,
                        Column = col,
                        IsActive = true
                    });
                }
            }
        }

        _context.Seats.AddRange(seats);
        vessel.SeatsConfigured = true;
        await _context.SaveChangesAsync(cancellationToken);

        return SeatSupport.CreateVesselSeatsDto(vessel, seats);
    }
}
