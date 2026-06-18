using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Application.Vessels;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Application.Seats;

internal sealed record SeatLayoutPlan(
    IReadOnlyCollection<Seat> Seats,
    IReadOnlyCollection<VesselFacility> Facilities,
    IReadOnlyCollection<VesselLayoutCell> LayoutCells);

internal static class SeatLayoutPlanner
{
    public static async Task<SeatLayoutPlan> BuildAsync(
        IApplicationDbContext context,
        Vessel vessel,
        IReadOnlyCollection<DeckConfigDto> decks,
        bool rejectExistingLayout,
        CancellationToken cancellationToken)
    {
        var seatTypes = await context.SeatTypes
            .ToListAsync(cancellationToken);
        var defaultSeatTypeDefinition = DefaultSeatTypeDefinition(vessel.SeatSetupType);
        var defaultSeatType = EnsureSeatType(
            context,
            seatTypes,
            defaultSeatTypeDefinition.Code,
            defaultSeatTypeDefinition.Name,
            defaultSeatTypeDefinition.DisplayOrder);

        var seatTypesByCode = seatTypes
            .Where(x => x.IsActive && IsAllowedSeatType(vessel.SeatSetupType, x.Code))
            .ToDictionary(x => x.Code, StringComparer.OrdinalIgnoreCase);

        if (rejectExistingLayout)
        {
            var hasExistingSeats = await context.Seats
                .AnyAsync(x => x.VesselId == vessel.Id, cancellationToken);
            var hasExistingFacilities = await context.VesselFacilities
                .AnyAsync(x => x.VesselId == vessel.Id, cancellationToken);

            if (hasExistingSeats || hasExistingFacilities)
                throw AuthSupport.CreateValidationException("Seats", "Tàu đã có ghế hoặc tiện ích. Xóa toàn bộ sơ đồ trước khi setup lại.");
        }

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

        var seats = new List<Seat>();
        var facilities = new List<VesselFacility>();
        var layoutCells = new List<VesselLayoutCell>();
        var seatCells = new HashSet<LayoutCell>();
        var facilityCells = new HashSet<LayoutCell>();
        var useExplicitCellsLayout = ShouldUseExplicitCellsLayout(vessel, decks);

        foreach (var deck in decks.OrderBy(d => d.DeckNumber))
        {
            if (deck.Cells is { Count: > 0 })
            {
                AddCellsLayout(
                    vessel,
                    vessel.SeatSetupType,
                    deck,
                    seatTypesByCode,
                    defaultSeatType,
                    seats,
                    facilities,
                    layoutCells,
                    seatCells,
                    facilityCells,
                    useExplicitCellsLayout);
                continue;
            }

            foreach (var seatCell in CreateSeatCells(deck, vessel.SeatSetupType, seatTypesByCode, defaultSeatType))
            {
                AddSeat(vessel, deck, seatCell, seats, seatCells, "SeatBlocks");
            }

            foreach (var facility in deck.Facilities ?? [])
            {
                AddFacility(vessel, deck, facility, facilities, seatCells, facilityCells);
            }
        }

        if (seats.Count != vessel.SeatCount)
            throw AuthSupport.CreateValidationException(
                "Cells",
                $"Tổng số ghế setup ({seats.Count}) không khớp với SeatCount của tàu ({vessel.SeatCount}).");

        return new SeatLayoutPlan(seats, facilities, layoutCells);
    }

    private static void AddCellsLayout(
        Vessel vessel,
        SeatSetupType seatSetupType,
        DeckConfigDto deck,
        IReadOnlyDictionary<string, SeatType> seatTypesByCode,
        SeatType defaultSeatType,
        List<Seat> seats,
        List<VesselFacility> facilities,
        List<VesselLayoutCell> layoutCells,
        HashSet<LayoutCell> seatCells,
        HashSet<LayoutCell> facilityCells,
        bool explicitCellsLayout)
    {
        var overridesByCell = new Dictionary<LayoutCell, LayoutCellConfigDto>();

        foreach (var cellConfig in deck.Cells!)
        {
            var currentCells = CreateLayoutCells(
                    deck.DeckNumber,
                    cellConfig.Row,
                    cellConfig.Column,
                    cellConfig.RowSpan,
                    cellConfig.ColumnSpan)
                .ToArray();

            EnsureCellShapeIsSupported(cellConfig, currentCells);

            foreach (var cell in currentCells)
            {
                EnsureCellInsideDeck(cell, deck);

                if (overridesByCell.ContainsKey(cell))
                    throw AuthSupport.CreateValidationException("Cells", "Các ô layout không được khai báo trùng nhau.");

                overridesByCell.Add(cell, cellConfig);
            }
        }

        for (var row = 1; row <= deck.RowCount; row++)
        {
            for (var column = 1; column <= deck.ColumnCount; column++)
            {
                var cell = new LayoutCell(deck.DeckNumber, row, column);
                if (!overridesByCell.TryGetValue(cell, out var cellConfig))
                {
                    if (explicitCellsLayout)
                    {
                        continue;
                    }

                    AddSeat(
                        vessel,
                        deck,
                        new SeatCellConfig(cell, defaultSeatType),
                        seats,
                        seatCells,
                        "Cells");
                    continue;
                }

                switch (cellConfig.Type)
                {
                    case SeatLayoutCellType.Seat:
                        var seatType = ResolveSeatTypeForCode(
                            seatSetupType,
                            cellConfig.SeatTypeCode,
                            seatTypesByCode,
                            defaultSeatType,
                            nameof(cellConfig.SeatTypeCode));
                        AddSeat(
                            vessel,
                            deck,
                            new SeatCellConfig(cell, seatType),
                            seats,
                            seatCells,
                            "Cells");
                        break;

                    case SeatLayoutCellType.Toilet:
                        if (row != cellConfig.Row || column != cellConfig.Column)
                        {
                            break;
                        }

                        AddFacility(
                            vessel,
                            deck,
                            new FacilityConfigDto(
                                VesselFacilityType.Toilet,
                                cellConfig.Row,
                                cellConfig.Column,
                                cellConfig.RowSpan,
                                cellConfig.ColumnSpan),
                            facilities,
                            seatCells,
                            facilityCells);
                        break;

                    case SeatLayoutCellType.Aisle:
                        AddLayoutCell(vessel, deck, cell, VesselLayoutCellType.Aisle, layoutCells);
                        break;

                    case SeatLayoutCellType.Empty:
                        AddLayoutCell(vessel, deck, cell, VesselLayoutCellType.Empty, layoutCells);
                        break;

                    default:
                        throw AuthSupport.CreateValidationException("Cells", "Loại ô layout không hợp lệ.");
                }
            }
        }
    }

    private static void AddLayoutCell(
        Vessel vessel,
        DeckConfigDto deck,
        LayoutCell cell,
        VesselLayoutCellType type,
        List<VesselLayoutCell> layoutCells)
    {
        EnsureCellInsideDeck(cell, deck);

        layoutCells.Add(new VesselLayoutCell
        {
            VesselId = vessel.Id,
            Type = type,
            Deck = deck.DeckNumber,
            Row = SeatSupport.RowLabel(cell.RowIndex - 1),
            Column = cell.Column
        });
    }

    private static bool ShouldUseExplicitCellsLayout(Vessel vessel, IReadOnlyCollection<DeckConfigDto> decks)
    {
        if (!decks.Any(deck => deck.Cells is { Count: > 0 }))
        {
            return false;
        }

        var explicitSeatCount = decks.Sum(deck =>
            deck.Cells is { Count: > 0 }
                ? deck.Cells.Count(cell => cell.Type == SeatLayoutCellType.Seat)
                : CountImplicitSeats(deck));

        return explicitSeatCount == vessel.SeatCount;
    }

    private static int CountImplicitSeats(DeckConfigDto deck)
    {
        if (deck.SeatBlocks is { Count: > 0 })
        {
            return deck.SeatBlocks.Sum(block => block.RowCount * block.ColumnCount);
        }

        return deck.RowCount * deck.ColumnCount;
    }

    private static void EnsureCellShapeIsSupported(LayoutCellConfigDto cellConfig, IReadOnlyCollection<LayoutCell> cells)
    {
        switch (cellConfig.Type)
        {
            case SeatLayoutCellType.Seat:
                if (cells.Count != 1)
                    throw AuthSupport.CreateValidationException("Cells", "Ô Seat chỉ được chiếm đúng 1 ô.");
                break;

            case SeatLayoutCellType.Toilet:
                if (cells.Count != 2)
                    throw AuthSupport.CreateValidationException("Cells", "Ô Toilet phải chiếm đúng 2 ô, theo chiều ngang hoặc chiều dọc.");
                break;

            case SeatLayoutCellType.Aisle:
            case SeatLayoutCellType.Empty:
                if (cells.Count != 1)
                    throw AuthSupport.CreateValidationException("Cells", "Ô Aisle/Empty chỉ được chiếm đúng 1 ô.");
                break;

            default:
                throw AuthSupport.CreateValidationException("Cells", "Loại ô layout không hợp lệ.");
        }
    }

    private static void AddSeat(
        Vessel vessel,
        DeckConfigDto deck,
        SeatCellConfig seatCell,
        List<Seat> seats,
        HashSet<LayoutCell> seatCells,
        string errorField)
    {
        var cell = seatCell.Cell;
        EnsureCellInsideDeck(cell, deck);

        if (!seatCells.Add(cell))
            throw AuthSupport.CreateValidationException(errorField, "Các ghế không được trùng ô nhau.");

        var row = SeatSupport.RowLabel(cell.RowIndex - 1);
        seats.Add(new Seat
        {
            VesselId = vessel.Id,
            SeatTypeId = seatCell.SeatType.Id,
            SeatType = seatCell.SeatType,
            Code = SeatSupport.SeatCode(deck.DeckNumber, row, cell.Column),
            Deck = deck.DeckNumber,
            Row = row,
            Column = cell.Column,
            IsActive = true
        });
    }

    private static void AddFacility(
        Vessel vessel,
        DeckConfigDto deck,
        FacilityConfigDto facility,
        List<VesselFacility> facilities,
        HashSet<LayoutCell> seatCells,
        HashSet<LayoutCell> facilityCells)
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

    private static IEnumerable<SeatCellConfig> CreateSeatCells(
        DeckConfigDto deck,
        SeatSetupType seatSetupType,
        IReadOnlyDictionary<string, SeatType> seatTypesByCode,
        SeatType defaultSeatType)
    {
        var hasSeatBlocks = deck.SeatBlocks is { Count: > 0 };
        if (!hasSeatBlocks)
        {
            for (var row = 1; row <= deck.RowCount; row++)
            {
                for (var column = 1; column <= deck.ColumnCount; column++)
                {
                    yield return new SeatCellConfig(new LayoutCell(deck.DeckNumber, row, column), defaultSeatType);
                }
            }

            yield break;
        }

        foreach (var block in deck.SeatBlocks!)
        {
            var seatType = ResolveSeatTypeForBlock(seatSetupType, block, seatTypesByCode, defaultSeatType);
            for (var row = block.StartRow; row < block.StartRow + block.RowCount; row++)
            {
                for (var column = block.StartColumn; column < block.StartColumn + block.ColumnCount; column++)
                {
                    yield return new SeatCellConfig(new LayoutCell(deck.DeckNumber, row, column), seatType);
                }
            }
        }
    }

    internal static SeatType EnsureSeatType(
        IApplicationDbContext? context,
        ICollection<SeatType> seatTypes,
        string code,
        string name,
        int displayOrder)
    {
        var seatType = seatTypes.FirstOrDefault(x =>
            string.Equals(x.Code, code, StringComparison.OrdinalIgnoreCase));
        if (seatType is not null)
        {
            seatType.IsActive = true;
            return seatType;
        }

        seatType = new SeatType
        {
            Code = code,
            Name = name,
            DisplayOrder = displayOrder,
            IsActive = true
        };
        seatTypes.Add(seatType);
        context?.SeatTypes.Add(seatType);

        return seatType;
    }

    private static SeatType ResolveSeatTypeForBlock(
        SeatSetupType seatSetupType,
        SeatBlockDto block,
        IReadOnlyDictionary<string, SeatType> seatTypesByCode,
        SeatType defaultSeatType)
    {
        return ResolveSeatTypeForCode(
            seatSetupType,
            block.SeatTypeCode,
            seatTypesByCode,
            defaultSeatType,
            nameof(block.SeatTypeCode));
    }

    private static SeatType ResolveSeatTypeForCode(
        SeatSetupType seatSetupType,
        string? seatTypeCode,
        IReadOnlyDictionary<string, SeatType> seatTypesByCode,
        SeatType defaultSeatType,
        string errorField)
    {
        if (string.IsNullOrWhiteSpace(seatTypeCode))
        {
            return defaultSeatType;
        }

        var normalizedCode = seatTypeCode.Trim().ToUpperInvariant();
        if (!IsAllowedSeatType(seatSetupType, normalizedCode))
        {
            throw AuthSupport.CreateValidationException(
                errorField,
                "Kiểu ghế FullStandard chỉ hỗ trợ STANDARD.");
        }

        if (seatTypesByCode.TryGetValue(normalizedCode, out var seatType))
        {
            return seatType;
        }

        throw AuthSupport.CreateValidationException(
            errorField,
            $"Loại ghế '{seatTypeCode}' không hợp lệ với kiểu ghế {seatSetupType}.");
    }

    internal static bool IsAllowedSeatType(SeatSetupType seatSetupType, string code) =>
        seatSetupType != SeatSetupType.FullStandard
        || string.Equals(code, "STANDARD", StringComparison.OrdinalIgnoreCase);

    internal static SeatTypeDefinition DefaultSeatTypeDefinition(SeatSetupType seatSetupType) =>
        seatSetupType == SeatSetupType.FullStandard
            ? new SeatTypeDefinition("STANDARD", "Standard Seat", 1)
            : new SeatTypeDefinition("CABIN", "Cabin Seat", 2);

    private static IEnumerable<LayoutCell> CreateFacilityCells(int deckNumber, FacilityConfigDto facility)
    {
        return CreateLayoutCells(deckNumber, facility.StartRow, facility.StartColumn, facility.RowSpan, facility.ColumnSpan);
    }

    private static IEnumerable<LayoutCell> CreateLayoutCells(
        int deckNumber,
        int startRow,
        int startColumn,
        int rowSpan,
        int columnSpan)
    {
        for (var row = startRow; row < startRow + rowSpan; row++)
        {
            for (var column = startColumn; column < startColumn + columnSpan; column++)
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

internal sealed record SeatTypeDefinition(string Code, string Name, int DisplayOrder);
