using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Application.Seats;

internal sealed record SeatLayoutPlan(
    IReadOnlyCollection<Seat> Seats,
    IReadOnlyCollection<VesselFacility> Facilities);

internal static class SeatLayoutPlanner
{
    public static async Task<SeatLayoutPlan> BuildAsync(
        IApplicationDbContext context,
        Vessel vessel,
        IReadOnlyCollection<DeckConfigDto> decks,
        bool rejectExistingLayout,
        CancellationToken cancellationToken)
    {
        if (!vessel.WaterbusServiceId.HasValue || vessel.WaterbusService is null)
        {
            throw AuthSupport.CreateValidationException(
                "WaterbusServiceId",
                "Tàu phải được gắn dịch vụ trước khi setup ghế.");
        }

        var service = vessel.WaterbusService;
        var seatTypes = await context.SeatTypes
            .Where(x => x.WaterbusServiceId == vessel.WaterbusServiceId.Value && x.IsActive)
            .ToListAsync(cancellationToken);
        var defaultSeatType = ResolveDefaultSeatType(service, seatTypes);
        var seatTypesByCode = seatTypes.ToDictionary(x => x.Code, StringComparer.OrdinalIgnoreCase);

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
        var seatCells = new HashSet<LayoutCell>();
        var facilityCells = new HashSet<LayoutCell>();

        foreach (var deck in decks.OrderBy(d => d.DeckNumber))
        {
            if (deck.Cells is { Count: > 0 })
            {
                AddCellsLayout(
                    vessel,
                    service,
                    deck,
                    seatTypesByCode,
                    defaultSeatType,
                    seats,
                    facilities,
                    seatCells,
                    facilityCells);
                continue;
            }

            foreach (var seatCell in CreateSeatCells(deck, service, seatTypesByCode, defaultSeatType))
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

        return new SeatLayoutPlan(seats, facilities);
    }

    private static void AddCellsLayout(
        Vessel vessel,
        WaterbusService service,
        DeckConfigDto deck,
        IReadOnlyDictionary<string, SeatType> seatTypesByCode,
        SeatType defaultSeatType,
        List<Seat> seats,
        List<VesselFacility> facilities,
        HashSet<LayoutCell> seatCells,
        HashSet<LayoutCell> facilityCells)
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
                            service,
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
                    case SeatLayoutCellType.Empty:
                        break;

                    default:
                        throw AuthSupport.CreateValidationException("Cells", "Loại ô layout không hợp lệ.");
                }
            }
        }
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
        WaterbusService service,
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
            var seatType = ResolveSeatTypeForBlock(service, block, seatTypesByCode, defaultSeatType);
            for (var row = block.StartRow; row < block.StartRow + block.RowCount; row++)
            {
                for (var column = block.StartColumn; column < block.StartColumn + block.ColumnCount; column++)
                {
                    yield return new SeatCellConfig(new LayoutCell(deck.DeckNumber, row, column), seatType);
                }
            }
        }
    }

    private static SeatType ResolveDefaultSeatType(WaterbusService service, IReadOnlyCollection<SeatType> seatTypes)
    {
        var standard = seatTypes.FirstOrDefault(x => string.Equals(x.Code, "STANDARD", StringComparison.OrdinalIgnoreCase));
        if (standard is not null)
        {
            return standard;
        }

        throw AuthSupport.CreateValidationException(
            "SeatTypes",
            $"Dịch vụ {service.Code} cần loại ghế STANDARD trước khi generate seats.");
    }

    private static SeatType ResolveSeatTypeForBlock(
        WaterbusService service,
        SeatBlockDto block,
        IReadOnlyDictionary<string, SeatType> seatTypesByCode,
        SeatType defaultSeatType)
    {
        return ResolveSeatTypeForCode(
            service,
            block.SeatTypeCode,
            seatTypesByCode,
            defaultSeatType,
            nameof(block.SeatTypeCode));
    }

    private static SeatType ResolveSeatTypeForCode(
        WaterbusService service,
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
        if (service.BookingMode is BookingMode.SeatBased
            && !string.Equals(normalizedCode, "STANDARD", StringComparison.OrdinalIgnoreCase))
        {
            throw AuthSupport.CreateValidationException(errorField, "Waterbus chỉ hỗ trợ loại ghế STANDARD.");
        }

        if (seatTypesByCode.TryGetValue(normalizedCode, out var seatType))
        {
            return seatType;
        }

        throw AuthSupport.CreateValidationException(
            errorField,
            $"Loại ghế '{seatTypeCode}' không thuộc dịch vụ {service.Code}.");
    }

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
