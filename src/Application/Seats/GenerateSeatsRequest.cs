using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Application.Boats;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Application.Seats;

public enum SeatLayoutCellType
{
    Empty = 1,
    Aisle = 2,
    Seat = 3
}

public sealed record LayoutCellConfigDto(
    int Row,
    int Column,
    SeatLayoutCellType Type,
    string? SeatTypeCode = null);

public sealed record DeckConfigDto(
    int DeckNumber,
    int RowCount,
    int ColumnCount,
    IReadOnlyCollection<LayoutCellConfigDto>? Cells = null);

public sealed record GenerateSeatsRequest(Guid BoatId, IReadOnlyCollection<DeckConfigDto> Decks);

public sealed class GenerateSeatsRequestValidator : AbstractValidator<GenerateSeatsRequest>
{
    public GenerateSeatsRequestValidator()
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

            deck.RuleFor(d => d)
                .Must(d => d.Cells is null ||
                           d.Cells.Select(c => (c.Row, c.Column)).Distinct().Count() == d.Cells.Count)
                .WithMessage("Các ô layout trong cùng tầng không được trùng vị trí.")
                .When(d => d.Cells is { Count: > 0 });

            deck.RuleFor(d => d)
                .Must(d => d.Cells is null ||
                           d.Cells.All(c => c.Row <= d.RowCount && c.Column <= d.ColumnCount))
                .WithMessage("Ô layout không được vượt quá kích thước tầng.")
                .When(d => d.Cells is { Count: > 0 });

            deck.RuleForEach(d => d.Cells).ChildRules(cell =>
            {
                cell.RuleFor(c => c.Row)
                    .GreaterThan(0)
                    .WithMessage("Hàng của ô layout phải lớn hơn 0.");

                cell.RuleFor(c => c.Column)
                    .GreaterThan(0)
                    .WithMessage("Cột của ô layout phải lớn hơn 0.");

                cell.RuleFor(c => c.Type)
                    .IsInEnum()
                    .WithMessage("Loại ô layout không hợp lệ.");

                cell.RuleFor(c => c)
                    .Must(c => c.Type == SeatLayoutCellType.Seat || string.IsNullOrWhiteSpace(c.SeatTypeCode))
                    .WithMessage("Chỉ ô Seat mới được gửi SeatTypeCode.");

            }).When(d => d.Cells is not null);
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

    public async Task<BoatSeatsDto> ExecuteAsync(GenerateSeatsRequest request, CancellationToken cancellationToken)
    {
        await SeatSupport.EnsureCurrentUserCanManageSeatsAsync(_context, _userContext, cancellationToken);

        var boat = await _context.Boats
            .SingleOrDefaultAsync(x => x.Id == request.BoatId, cancellationToken)
            ?? throw new SaigonWaterbus.Application.Common.Exceptions.NotFoundException("Không tìm thấy tàu.");

        var hasExistingSeats = await _context.Seats.AnyAsync(x => x.BoatId == boat.Id, cancellationToken);
        if (hasExistingSeats)
        {
            throw AuthSupport.CreateValidationException("Seats", "Tàu đã có ghế. Xóa toàn bộ trước khi configure lại.");
        }

        GenerateSeatMatrixRequestUseCase.EnsureDecksMatchBoat(
            boat,
            request.Decks.Select(x => new DeckMatrixConfigDto(x.DeckNumber, x.RowCount, x.ColumnCount)).ToArray());

        var seatTypeById = await _context.Set<SeatType>()
            .Where(st => st.IsActive)
            .ToDictionaryAsync(st => st.Code, cancellationToken);

        var seats = CreateSeats(boat, request.Decks, seatTypeById);
        if (seats.Count == 0)
        {
            throw AuthSupport.CreateValidationException(
                "Seats",
                "Cần chọn ít nhất 1 ô ghế.");
        }

        _context.Seats.AddRange(seats);
        boat.SeatCount = seats.Count;
        boat.SeatsConfigured = true;
        boat.Status = BoatStatus.Active;
        await _context.SaveChangesAsync(cancellationToken);

        return SeatSupport.CreateBoatSeatsDto(
            boat,
            seats,
            CreateDeckLayouts(boat, request.Decks));
    }

    private static List<Seat> CreateSeats(Boat boat, IReadOnlyCollection<DeckConfigDto> decks, Dictionary<string, SeatType> seatTypeById)
    {
        var seats = new List<Seat>();
        var occupiedCells = new HashSet<(int Deck, int Row, int Column)>();

        foreach (var deck in decks.OrderBy(x => x.DeckNumber))
        {
            var cellOverrides = new Dictionary<(int Row, int Column), LayoutCellConfigDto>();
            if (deck.Cells is { Count: > 0 })
            {
                foreach (var cell in deck.Cells)
                {
                    EnsureCellWithinDeck(deck, cell.Row, cell.Column);
                    if (!cellOverrides.TryAdd((cell.Row, cell.Column), cell))
                    {
                        throw AuthSupport.CreateValidationException(
                            "Decks",
                            $"Ô tầng {deck.DeckNumber}, hàng {cell.Row}, cột {cell.Column} bị trùng.");
                    }
                }
            }

            for (var row = 1; row <= deck.RowCount; row++)
            {
                for (var column = 1; column <= deck.ColumnCount; column++)
                {
                    if (cellOverrides.TryGetValue((row, column), out var cell))
                    {
                        if (cell.Type != SeatLayoutCellType.Seat)
                        {
                            continue;
                        }

                        AddSeat(boat, deck.DeckNumber, row, column, cell.SeatTypeCode, seats, occupiedCells, seatTypeById);
                        continue;
                    }

                    AddSeat(boat, deck.DeckNumber, row, column, null, seats, occupiedCells, seatTypeById);
                }
            }
        }

        return seats;
    }

    private static SeatDeckLayout[] CreateDeckLayouts(Boat boat, IReadOnlyCollection<DeckConfigDto> decks) =>
        decks
            .OrderBy(x => x.DeckNumber)
            .Select(x => new SeatDeckLayout(x.DeckNumber, x.RowCount, x.ColumnCount))
            .ToArray();

    private static void AddSeat(
        Boat boat,
        int deckNumber,
        int row,
        int column,
        string? seatTypeCode,
        List<Seat> seats,
        HashSet<(int Deck, int Row, int Column)> occupiedCells,
        Dictionary<string, SeatType> seatTypeByCode)
    {
        if (!occupiedCells.Add((deckNumber, row, column)))
        {
            throw AuthSupport.CreateValidationException("Seats", $"Vị trí ghế tầng {deckNumber}, hàng {row}, cột {column} bị trùng.");
        }

        var rowLabel = SeatSupport.RowLabel(row - 1);
        var normalizedCode = SeatSupport.NormalizeSeatTypeCode(seatTypeCode, boat.SeatSetupType);
        seatTypeByCode.TryGetValue(normalizedCode, out var seatType);

        seats.Add(new Seat
        {
            BoatId = boat.Id,
            Code = SeatSupport.SeatCode(deckNumber, rowLabel, column),
            SeatTypeCode = normalizedCode,
            SeatTypeId = seatType?.Id,
            Deck = deckNumber,
            Row = rowLabel,
            Column = column,
            IsActive = true
        });
    }

    private static void EnsureCellWithinDeck(DeckConfigDto deck, int row, int column)
    {
        if (row > deck.RowCount || column > deck.ColumnCount)
        {
            throw AuthSupport.CreateValidationException(
                "Decks",
                $"Ô hàng {row}, cột {column} vượt quá kích thước tầng {deck.DeckNumber} ({deck.RowCount}x{deck.ColumnCount}).");
        }
    }
}
