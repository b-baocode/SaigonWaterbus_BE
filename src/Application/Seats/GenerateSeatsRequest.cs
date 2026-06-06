using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Application.Seats;

public sealed record SeatBlockDto(int StartRow, int StartColumn, int RowCount, int ColumnCount);

public sealed record FacilityConfigDto(
    VesselFacilityType Type,
    int StartRow,
    int StartColumn,
    int RowSpan,
    int ColumnSpan);

public sealed record DeckConfigDto(
    int DeckNumber,
    int RowCount,
    int ColumnCount,
    IReadOnlyCollection<SeatBlockDto>? SeatBlocks = null,
    IReadOnlyCollection<FacilityConfigDto>? Facilities = null);

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

            deck.RuleForEach(d => d.SeatBlocks).ChildRules(block =>
            {
                block.RuleFor(b => b.StartRow)
                    .GreaterThan(0)
                    .WithMessage("Hàng bắt đầu của vùng ghế phải lớn hơn 0.");

                block.RuleFor(b => b.StartColumn)
                    .GreaterThan(0)
                    .WithMessage("Cột bắt đầu của vùng ghế phải lớn hơn 0.");

                block.RuleFor(b => b.RowCount)
                    .GreaterThan(0)
                    .WithMessage("Số hàng của vùng ghế phải lớn hơn 0.");

                block.RuleFor(b => b.ColumnCount)
                    .GreaterThan(0)
                    .WithMessage("Số cột của vùng ghế phải lớn hơn 0.");
            }).When(d => d.SeatBlocks is not null);

            deck.RuleForEach(d => d.Facilities).ChildRules(facility =>
            {
                facility.RuleFor(f => f.Type)
                    .IsInEnum()
                    .WithMessage("Loại tiện ích không hợp lệ.");

                facility.RuleFor(f => f.StartRow)
                    .GreaterThan(0)
                    .WithMessage("Hàng bắt đầu của tiện ích phải lớn hơn 0.");

                facility.RuleFor(f => f.StartColumn)
                    .GreaterThan(0)
                    .WithMessage("Cột bắt đầu của tiện ích phải lớn hơn 0.");

                facility.RuleFor(f => f.RowSpan)
                    .GreaterThan(0)
                    .WithMessage("RowSpan của tiện ích phải lớn hơn 0.");

                facility.RuleFor(f => f.ColumnSpan)
                    .GreaterThan(0)
                    .WithMessage("ColumnSpan của tiện ích phải lớn hơn 0.");

                facility.RuleFor(f => f)
                    .Must(f => f.RowSpan * f.ColumnSpan == 2)
                    .WithMessage("WC phải chiếm đúng 2 ô, theo chiều ngang hoặc chiều dọc.");
            }).When(d => d.Facilities is not null);
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
        var hasExistingDeckLayouts = await _context.VesselDeckLayouts
            .AnyAsync(x => x.VesselId == request.VesselId, cancellationToken);
        var hasExistingFacilities = await _context.VesselFacilities
            .AnyAsync(x => x.VesselId == request.VesselId, cancellationToken);

        if (hasExistingSeats || hasExistingDeckLayouts || hasExistingFacilities)
            throw AuthSupport.CreateValidationException("Seats", "Tàu đã có sơ đồ ghế. Xóa toàn bộ sơ đồ trước khi generate lại.");

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

        var seats = new List<Seat>();
        var deckLayouts = new List<VesselDeckLayout>();
        var facilities = new List<VesselFacility>();
        var seatCells = new HashSet<LayoutCell>();
        var facilityCells = new HashSet<LayoutCell>();

        foreach (var deck in request.Decks.OrderBy(d => d.DeckNumber))
        {
            deckLayouts.Add(new VesselDeckLayout
            {
                VesselId = vessel.Id,
                DeckNumber = deck.DeckNumber,
                RowCount = deck.RowCount,
                ColumnCount = deck.ColumnCount
            });

            foreach (var cell in CreateSeatCells(deck))
            {
                EnsureCellInsideDeck(cell, deck);

                if (!seatCells.Add(cell))
                    throw AuthSupport.CreateValidationException("SeatBlocks", "Các vùng ghế không được trùng ô nhau.");

                var row = SeatSupport.RowLabel(cell.RowIndex - 1);
                seats.Add(new Seat
                {
                    VesselId = vessel.Id,
                    Code = SeatSupport.SeatCode(deck.DeckNumber, row, cell.Column),
                    Deck = deck.DeckNumber,
                    Row = row,
                    Column = cell.Column,
                    IsActive = true
                });
            }

            foreach (var facility in deck.Facilities ?? [])
            {
                EnsureSupportedFacility(facility);

                foreach (var cell in CreateFacilityCells(deck.DeckNumber, facility))
                {
                    EnsureCellInsideDeck(cell, deck);

                    if (!facilityCells.Add(cell))
                        throw AuthSupport.CreateValidationException("Facilities", "Các tiện ích không được trùng ô nhau.");

                    if (seatCells.Contains(cell))
                        throw AuthSupport.CreateValidationException("Facilities", "Tiện ích không được đặt đè lên ghế.");
                }

                facilities.Add(new VesselFacility
                {
                    VesselId = vessel.Id,
                    Type = facility.Type,
                    Deck = deck.DeckNumber,
                    Row = SeatSupport.RowLabel(facility.StartRow - 1),
                    Column = facility.StartColumn,
                    RowSpan = facility.RowSpan,
                    ColumnSpan = facility.ColumnSpan,
                    IsActive = true
                });
            }
        }

        if (seats.Count != vessel.SeatCount)
            throw AuthSupport.CreateValidationException(
                "SeatBlocks",
                $"Tổng số ghế setup ({seats.Count}) không khớp với SeatCount của tàu ({vessel.SeatCount}).");

        _context.VesselDeckLayouts.AddRange(deckLayouts);
        _context.Seats.AddRange(seats);
        _context.VesselFacilities.AddRange(facilities);
        vessel.SeatsConfigured = true;
        await _context.SaveChangesAsync(cancellationToken);

        return SeatSupport.CreateVesselSeatsDto(vessel, seats, deckLayouts, facilities);
    }

    private static IEnumerable<LayoutCell> CreateSeatCells(DeckConfigDto deck)
    {
        var hasSeatBlocks = deck.SeatBlocks is { Count: > 0 };
        if (!hasSeatBlocks)
        {
            for (var row = 1; row <= deck.RowCount; row++)
            {
                for (var column = 1; column <= deck.ColumnCount; column++)
                {
                    yield return new LayoutCell(deck.DeckNumber, row, column);
                }
            }

            yield break;
        }

        foreach (var block in deck.SeatBlocks!)
        {
            for (var row = block.StartRow; row < block.StartRow + block.RowCount; row++)
            {
                for (var column = block.StartColumn; column < block.StartColumn + block.ColumnCount; column++)
                {
                    yield return new LayoutCell(deck.DeckNumber, row, column);
                }
            }
        }
    }

    private static IEnumerable<LayoutCell> CreateFacilityCells(int deckNumber, FacilityConfigDto facility)
    {
        for (var row = facility.StartRow; row < facility.StartRow + facility.RowSpan; row++)
        {
            for (var column = facility.StartColumn; column < facility.StartColumn + facility.ColumnSpan; column++)
            {
                yield return new LayoutCell(deckNumber, row, column);
            }
        }
    }

    private static void EnsureCellInsideDeck(LayoutCell cell, DeckConfigDto deck)
    {
        if (cell.RowIndex > deck.RowCount || cell.Column > deck.ColumnCount)
            throw AuthSupport.CreateValidationException("Decks", "Vùng ghế hoặc tiện ích vượt ra ngoài ma trận tầng.");
    }

    private static void EnsureSupportedFacility(FacilityConfigDto facility)
    {
        if (facility.Type != VesselFacilityType.Toilet)
            throw AuthSupport.CreateValidationException("Facilities", "Hiện tại chỉ hỗ trợ tiện ích Toilet.");

        if (facility.RowSpan * facility.ColumnSpan != 2)
            throw AuthSupport.CreateValidationException("Facilities", "WC phải chiếm đúng 2 ô, theo chiều ngang hoặc chiều dọc.");
    }
}

internal readonly record struct LayoutCell(int DeckNumber, int RowIndex, int Column);
