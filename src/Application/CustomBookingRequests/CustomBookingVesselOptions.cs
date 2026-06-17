using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Application.Vessels;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using ForbiddenAccessException = SaigonWaterbus.Application.Common.Exceptions.ForbiddenAccessException;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;

namespace SaigonWaterbus.Application.CustomBookingRequests;

public sealed record CustomBookingPriceRangeDto(
    string Currency,
    VesselRentalUnit RentalUnit,
    decimal MinimumPrice,
    decimal MaximumPrice);

public sealed record CustomBookingPricingOptionsDto(
    int RequestedNumberOfDecks,
    SeatSetupType RequestedSeatSetupType,
    VesselRentalUnit RentalUnit,
    int PassengerCount,
    int MatchingVesselCount,
    IReadOnlyCollection<CustomBookingPriceRangeDto> PriceRanges,
    string Note);

public sealed record GetCustomBookingPricingOptionsQuery(
    int RequestedNumberOfDecks,
    SeatSetupType RequestedSeatSetupType,
    VesselRentalUnit RentalUnit,
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
        RuleFor(x => x.RentalUnit)
            .IsInEnum()
            .WithMessage("Đơn vị thuê tàu chỉ được là Hour hoặc Day.");
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
                request.RentalUnit,
                request.PassengerCount)
            .SelectMany(x => x.RentalPrices
                .Where(price => price.RentalUnit == request.RentalUnit)
                .Select(price => new { x.Id, price.RentalUnit, price.UnitPrice, price.Currency }))
            .ToListAsync(cancellationToken);
        var priceRanges = prices
            .GroupBy(x => new { Currency = x.Currency.ToUpperInvariant(), x.RentalUnit })
            .OrderBy(x => x.Key.Currency)
            .ThenBy(x => VesselSupport.RentalUnitDisplayOrder(x.Key.RentalUnit))
            .Select(group => new CustomBookingPriceRangeDto(
                group.Key.Currency,
                group.Key.RentalUnit,
                group.Min(x => x.UnitPrice),
                group.Max(x => x.UnitPrice)))
            .ToArray();

        return new CustomBookingPricingOptionsDto(
            request.RequestedNumberOfDecks,
            request.RequestedSeatSetupType,
            request.RentalUnit,
            request.PassengerCount,
            prices.Select(x => x.Id).Distinct().Count(),
            priceRanges,
            prices.Count == 0
                ? "Hiện chưa có tàu phù hợp đã cấu hình giá theo đơn vị thuê khách chọn. Khách vẫn có thể gửi yêu cầu để Admin kiểm tra thủ công."
                : "Đây là giá thuê tàu tham khảo theo đơn vị thuê khách chọn, giá cuối sẽ được hệ thống tính sau khi Admin gán tàu.");
    }
}

public sealed record CustomBookingVesselRentalPriceOptionDto(
    VesselRentalUnit RentalUnit,
    decimal UnitPrice,
    decimal EstimatedBasePrice,
    string Currency,
    string? PriceNote);

public sealed record CustomBookingVesselCandidateDto(
    Guid VesselId,
    string Code,
    string Name,
    int SeatCount,
    int NumberOfDecks,
    SeatSetupType SeatSetupType,
    string ImageUrl,
    IReadOnlyCollection<CustomBookingVesselRentalPriceOptionDto> RentalPrices);

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
        var actor = await AuthSupport.GetCurrentUserWithRoleAsync(_context, _userContext, cancellationToken);
        var customRequest = await _context.Set<CustomBookingRequest>()
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy yêu cầu thuê tàu.");

        if (!AuthSupport.IsAdmin(actor)
            && (!AuthSupport.IsCustomer(actor) || customRequest.UserId != actor.Id))
        {
            throw new ForbiddenAccessException();
        }

        CustomBookingRequestSupport.EnsureCanAssignVessel(customRequest);

        var unavailableVesselIds = await CustomBookingAvailability.GetUnavailableVesselIdsAsync(
            _context,
            customRequest,
            customRequest.Id,
            cancellationToken);
        var unavailableVesselIdArray = unavailableVesselIds.ToArray();
        var vessels = await CustomBookingVesselMatcher.Apply(
                _context.Set<Vessel>().AsNoTracking().Include(x => x.RentalPrices),
                customRequest.RequestedNumberOfDecks,
                customRequest.RequestedSeatSetupType,
                customRequest.RentalUnit,
                customRequest.PassengerCount)
            .Where(x => !unavailableVesselIdArray.Contains(x.Id))
            .OrderBy(x => x.SeatCount)
            .ThenBy(x => x.Code)
            .ToListAsync(cancellationToken);

        return vessels
            .Select(vessel =>
            {
                var rentalPrices = vessel.RentalPrices
                    .Where(x => x.RentalUnit == customRequest.RentalUnit)
                    .OrderBy(x => VesselSupport.RentalUnitDisplayOrder(x.RentalUnit))
                    .ThenBy(x => x.Id)
                    .Select(price => new CustomBookingVesselRentalPriceOptionDto(
                        price.RentalUnit,
                        price.UnitPrice,
                        EstimateBasePrice(customRequest, price),
                        price.Currency,
                        price.Note))
                    .ToArray();

                return new CustomBookingVesselCandidateDto(
                    vessel.Id,
                    vessel.Code,
                    vessel.Name,
                    vessel.SeatCount,
                    vessel.NumberOfDecks,
                    vessel.SeatSetupType,
                    vessel.ImageUrl ?? string.Empty,
                    rentalPrices);
            })
            .ToArray();
    }

    private static decimal EstimateBasePrice(
        CustomBookingRequest request,
        VesselRentalPrice price) =>
        CustomBookingRequestSupport.CalculateRentalPrice(request, price);
}

internal static class CustomBookingVesselMatcher
{
    public static IQueryable<Vessel> Apply(
        IQueryable<Vessel> query,
        int numberOfDecks,
        SeatSetupType seatSetupType,
        VesselRentalUnit rentalUnit,
        int passengerCount) =>
        query.Where(x =>
            x.Status == VesselStatus.Active
            && x.SeatsConfigured
            && x.NumberOfDecks == numberOfDecks
            && x.SeatSetupType == seatSetupType
            && x.SeatCount >= passengerCount
            && x.RentalPrices.Any(price => price.RentalUnit == rentalUnit));
}
