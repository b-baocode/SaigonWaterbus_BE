using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Application.Vessels;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Application.Seats;

public sealed record SeatBlockDto(
    int StartRow,
    int StartColumn,
    int RowCount,
    int ColumnCount,
    string? SeatTypeCode = null);

public sealed record FacilityConfigDto(
    VesselFacilityType Type,
    int StartRow,
    int StartColumn,
    int RowSpan,
    int ColumnSpan);

public enum SeatLayoutCellType
{
    Empty = 1,
    Aisle = 2,
    Seat = 3,
    Toilet = 4
}

public sealed record LayoutCellConfigDto(
    int Row,
    int Column,
    SeatLayoutCellType Type,
    string? SeatTypeCode = null,
    int RowSpan = 1,
    int ColumnSpan = 1);

public sealed record DeckConfigDto(
    int DeckNumber,
    int RowCount,
    int ColumnCount,
    IReadOnlyCollection<SeatBlockDto>? SeatBlocks = null,
    IReadOnlyCollection<FacilityConfigDto>? Facilities = null,
    IReadOnlyCollection<LayoutCellConfigDto>? Cells = null);

public sealed record GenerateSeatsRequest(Guid VesselId, IReadOnlyCollection<DeckConfigDto> Decks);

public sealed class GenerateSeatsRequestValidator : AbstractValidator<GenerateSeatsRequest>
{
    public GenerateSeatsRequestValidator()
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

            deck.RuleFor(d => d)
                .Must(d =>
                    d.Cells is not { Count: > 0 }
                    || ((d.SeatBlocks is null || d.SeatBlocks.Count == 0)
                        && (d.Facilities is null || d.Facilities.Count == 0)))
                .WithMessage("Khi dùng cells thì không gửi seatBlocks/facilities để tránh cấu hình bị lẫn logic.");

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

                cell.RuleFor(c => c.RowSpan)
                    .GreaterThan(0)
                    .WithMessage("RowSpan của ô layout phải lớn hơn 0.");

                cell.RuleFor(c => c.ColumnSpan)
                    .GreaterThan(0)
                    .WithMessage("ColumnSpan của ô layout phải lớn hơn 0.");

                cell.RuleFor(c => c)
                    .Must(c => c.Type != SeatLayoutCellType.Seat || (c.RowSpan == 1 && c.ColumnSpan == 1))
                    .WithMessage("Ô Seat chỉ được chiếm đúng 1 ô.");

                cell.RuleFor(c => c)
                    .Must(c => c.Type is not (SeatLayoutCellType.Aisle or SeatLayoutCellType.Empty)
                        || (c.RowSpan == 1 && c.ColumnSpan == 1))
                    .WithMessage("Ô Aisle/Empty chỉ được chiếm đúng 1 ô.");

                cell.RuleFor(c => c)
                    .Must(c => c.Type != SeatLayoutCellType.Toilet || c.RowSpan * c.ColumnSpan == 2)
                    .WithMessage("Ô Toilet phải chiếm đúng 2 ô, theo chiều ngang hoặc chiều dọc.");

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

    public async Task<VesselSeatsDto> ExecuteAsync(GenerateSeatsRequest request, CancellationToken cancellationToken)
    {
        await SeatSupport.EnsureCurrentUserCanManageSeatsAsync(_context, _userContext, cancellationToken);

        var vessel = await _context.Vessels
            .SingleOrDefaultAsync(x => x.Id == request.VesselId, cancellationToken)
            ?? throw new SaigonWaterbus.Application.Common.Exceptions.NotFoundException("Không tìm thấy tàu.");

        var existingDeckLayouts = await _context.VesselDeckLayouts
            .Where(x => x.VesselId == vessel.Id)
            .ToListAsync(cancellationToken);

        if (existingDeckLayouts.Count == 0)
        {
            throw AuthSupport.CreateValidationException("Decks", "Tàu chưa có ma trận ghế. Gọi /seats/generate trước khi configure.");
        }

        EnsureRequestMatchesExistingMatrix(request.Decks, existingDeckLayouts);

        var plan = await SeatLayoutPlanner.BuildAsync(
            _context,
            vessel,
            request.Decks,
            rejectExistingLayout: true,
            cancellationToken);

        _context.Seats.AddRange(plan.Seats);
        _context.VesselFacilities.AddRange(plan.Facilities);
        vessel.SeatsConfigured = true;
        VesselSupport.EnsureCanActivate(vessel, nameof(vessel.Status));
        vessel.Status = VesselStatus.Active;
        await _context.SaveChangesAsync(cancellationToken);

        return SeatSupport.CreateVesselSeatsDto(
            vessel,
            plan.Seats.ToList(),
            existingDeckLayouts,
            plan.Facilities.ToList());
    }

    private static void EnsureRequestMatchesExistingMatrix(
        IReadOnlyCollection<DeckConfigDto> decks,
        IReadOnlyCollection<VesselDeckLayout> existingDeckLayouts)
    {
        var existingByDeck = existingDeckLayouts.ToDictionary(x => x.DeckNumber);

        foreach (var deck in decks)
        {
            if (!existingByDeck.TryGetValue(deck.DeckNumber, out var existing))
            {
                throw AuthSupport.CreateValidationException("Decks", $"Tầng {deck.DeckNumber} chưa có trong ma trận đã generate.");
            }

            if (existing.RowCount != deck.RowCount || existing.ColumnCount != deck.ColumnCount)
            {
                throw AuthSupport.CreateValidationException(
                    "Decks",
                    $"Tầng {deck.DeckNumber} không khớp ma trận đã generate. Ma trận hiện tại là {existing.RowCount} hàng x {existing.ColumnCount} cột.");
            }
        }
    }
}

internal readonly record struct LayoutCell(int DeckNumber, int RowIndex, int Column);

internal sealed record SeatCellConfig(LayoutCell Cell, SeatType SeatType);
