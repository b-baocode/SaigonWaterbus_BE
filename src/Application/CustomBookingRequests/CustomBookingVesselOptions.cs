using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;

namespace SaigonWaterbus.Application.CustomBookingRequests;

public sealed record CustomBookingPriceRangeDto(
    string Currency,
    decimal MinimumDailyPrice,
    decimal MaximumDailyPrice);

public sealed record CustomBookingPricingOptionsDto(
    int RequestedNumberOfDecks,
    SeatSetupType RequestedSeatSetupType,
    int PassengerCount,
    int MatchingVesselCount,
    VesselRentalUnit RentalUnit,
    IReadOnlyCollection<CustomBookingPriceRangeDto> PriceRanges,
    string Note);

public sealed record GetCustomBookingPricingOptionsQuery(
    int RequestedNumberOfDecks,
    SeatSetupType RequestedSeatSetupType,
    int PassengerCount) : IRequest<CustomBookingPricingOptionsDto>;

public sealed class GetCustomBookingPricingOptionsQueryValidator
    : AbstractValidator<GetCustomBookingPricingOptionsQuery>
{
    public GetCustomBookingPricingOptionsQueryValidator()
    {
        RuleFor(x => x.RequestedNumberOfDecks)
            .InclusiveBetween(1, 10)
            .WithMessage("Số tầng tàu yêu cầu phải từ 1 đến 10.");
        RuleFor(x => x.RequestedSeatSetupType)
            .IsInEnum()
            .WithMessage("Kiểu ghế yêu cầu chỉ nhận FullStandard hoặc StandardAndVip.");
        RuleFor(x => x.PassengerCount)
            .InclusiveBetween(1, 500)
            .WithMessage("Số khách phải từ 1 đến 500.");
    }
}

public sealed class GetCustomBookingPricingOptionsQueryHandler
    : IRequestHandler<GetCustomBookingPricingOptionsQuery, CustomBookingPricingOptionsDto>
{
    private readonly IApplicationDbContext _context;

    public GetCustomBookingPricingOptionsQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<CustomBookingPricingOptionsDto> Handle(
        GetCustomBookingPricingOptionsQuery request,
        CancellationToken cancellationToken)
    {
        var prices = await CustomBookingVesselMatcher.Apply(
                _context.Set<Vessel>().AsNoTracking(),
                request.RequestedNumberOfDecks,
                request.RequestedSeatSetupType,
                request.PassengerCount)
            .SelectMany(x => x.RentalPrices
                .Where(price => price.RentalUnit == VesselRentalUnit.Day)
                .Select(price => new { x.Id, price.UnitPrice, price.Currency }))
            .ToListAsync(cancellationToken);
        var priceRanges = prices
            .GroupBy(x => x.Currency.ToUpperInvariant())
            .OrderBy(x => x.Key)
            .Select(group => new CustomBookingPriceRangeDto(
                group.Key,
                group.Min(x => x.UnitPrice),
                group.Max(x => x.UnitPrice)))
            .ToArray();

        return new CustomBookingPricingOptionsDto(
            request.RequestedNumberOfDecks,
            request.RequestedSeatSetupType,
            request.PassengerCount,
            prices.Select(x => x.Id).Distinct().Count(),
            VesselRentalUnit.Day,
            priceRanges,
            prices.Count == 0
                ? "Hiện chưa có tàu phù hợp đã cấu hình giá. Khách vẫn có thể gửi yêu cầu để Admin kiểm tra thủ công."
                : "Đây là giá thuê tàu tham khảo theo ngày, chưa phải báo giá cuối cùng cho lịch trình.");
    }
}

public sealed record CustomBookingVesselCandidateDto(
    Guid VesselId,
    string Code,
    string Name,
    int SeatCount,
    int NumberOfDecks,
    SeatSetupType SeatSetupType,
    string ImageUrl,
    VesselRentalUnit RentalUnit,
    decimal DailyPrice,
    decimal EstimatedBasePrice,
    string Currency,
    string? PriceNote);

public sealed record GetCustomBookingVesselCandidatesQuery(Guid Id)
    : IRequest<IReadOnlyCollection<CustomBookingVesselCandidateDto>>;

public sealed class GetCustomBookingVesselCandidatesQueryValidator
    : AbstractValidator<GetCustomBookingVesselCandidatesQuery>
{
    public GetCustomBookingVesselCandidatesQueryValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty()
            .WithMessage("Id yêu cầu thuê tàu không hợp lệ.");
    }
}

public sealed class GetCustomBookingVesselCandidatesQueryHandler
    : IRequestHandler<GetCustomBookingVesselCandidatesQuery, IReadOnlyCollection<CustomBookingVesselCandidateDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;

    public GetCustomBookingVesselCandidatesQueryHandler(
        IApplicationDbContext context,
        IUserContext userContext)
    {
        _context = context;
        _userContext = userContext;
    }

    public async Task<IReadOnlyCollection<CustomBookingVesselCandidateDto>> Handle(
        GetCustomBookingVesselCandidatesQuery request,
        CancellationToken cancellationToken)
    {
        await CustomBookingRequestSupport.EnsureCurrentUserCanManageCustomBookingRequestsAsync(
            _context,
            _userContext,
            cancellationToken);
        var customRequest = await _context.Set<CustomBookingRequest>()
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy yêu cầu thuê tàu.");

        CustomBookingRequestSupport.EnsureCanAssignVessel(customRequest);

        var rentalDays = Math.Max(1, (int)Math.Ceiling(customRequest.EstimatedDurationMinutes / 1440m));
        var vessels = await CustomBookingVesselMatcher.Apply(
                _context.Set<Vessel>().AsNoTracking().Include(x => x.RentalPrices),
                customRequest.RequestedNumberOfDecks,
                customRequest.RequestedSeatSetupType,
                customRequest.PassengerCount)
            .OrderBy(x => x.SeatCount)
            .ThenBy(x => x.Code)
            .ToListAsync(cancellationToken);

        return vessels
            .Select(vessel =>
            {
                var price = vessel.RentalPrices.Single(x => x.RentalUnit == VesselRentalUnit.Day);
                return new CustomBookingVesselCandidateDto(
                    vessel.Id,
                    vessel.Code,
                    vessel.Name,
                    vessel.SeatCount,
                    vessel.NumberOfDecks,
                    vessel.SeatSetupType,
                    vessel.ImageUrl ?? string.Empty,
                    price.RentalUnit,
                    price.UnitPrice,
                    price.UnitPrice * rentalDays,
                    price.Currency,
                    price.Note);
            })
            .ToArray();
    }
}

internal static class CustomBookingVesselMatcher
{
    public static IQueryable<Vessel> Apply(
        IQueryable<Vessel> query,
        int numberOfDecks,
        SeatSetupType seatSetupType,
        int passengerCount) =>
        query.Where(x =>
            x.Status == VesselStatus.Active
            && x.SeatsConfigured
            && x.NumberOfDecks == numberOfDecks
            && x.SeatSetupType == seatSetupType
            && x.SeatCount >= passengerCount
            && x.RentalPrices.Any(price => price.RentalUnit == VesselRentalUnit.Day));
}
