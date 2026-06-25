using FluentValidation.Results;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.CustomBookings;

public sealed record GetRentalVesselEstimatesQuery(
    int AdultCount,
    int ChildCount,
    VesselRentalUnit RentalUnit = VesselRentalUnit.Day,
    int DurationValue = 1,
    int? PreferredNumberOfDecks = null,
    SeatSetupType? PreferredSeatSetupType = null)
    : IRequest<RentalVesselEstimateListDto>;

public sealed class GetRentalVesselEstimatesQueryValidator
    : AbstractValidator<GetRentalVesselEstimatesQuery>
{
    public GetRentalVesselEstimatesQueryValidator()
    {
        RuleFor(x => x.AdultCount)
            .GreaterThanOrEqualTo(0)
            .LessThanOrEqualTo(1000)
            .WithMessage("Số người lớn phải từ 0 đến 1000.");
        RuleFor(x => x.ChildCount)
            .GreaterThanOrEqualTo(0)
            .LessThanOrEqualTo(1000)
            .WithMessage("Số trẻ em phải từ 0 đến 1000.");
        RuleFor(x => x)
            .Must(x => x.AdultCount + x.ChildCount > 0)
            .WithMessage("Tổng số khách phải lớn hơn 0.");
        RuleFor(x => x)
            .Must(x => x.AdultCount + x.ChildCount <= 1000)
            .WithMessage("Tổng số khách không được vượt quá 1000.");
        RuleFor(x => x.RentalUnit).IsInEnum();
        RuleFor(x => x.DurationValue)
            .GreaterThan(0)
            .LessThanOrEqualTo(60)
            .WithMessage("Thời lượng thuê phải từ 1 đến 60.");
        RuleFor(x => x.PreferredNumberOfDecks)
            .InclusiveBetween(1, 10)
            .When(x => x.PreferredNumberOfDecks.HasValue)
            .WithMessage("Số tầng mong muốn phải từ 1 đến 10.");
        RuleFor(x => x.PreferredSeatSetupType)
            .IsInEnum()
            .When(x => x.PreferredSeatSetupType.HasValue);
    }
}

public sealed class GetRentalVesselEstimatesQueryHandler
    : IRequestHandler<GetRentalVesselEstimatesQuery, RentalVesselEstimateListDto>
{
    private readonly IApplicationDbContext _context;

    public GetRentalVesselEstimatesQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<RentalVesselEstimateListDto> Handle(
        GetRentalVesselEstimatesQuery request,
        CancellationToken cancellationToken)
    {
        var passengerCount = request.AdultCount + request.ChildCount;
        if (passengerCount <= 0)
        {
            throw new ValidationException([new ValidationFailure(nameof(request.AdultCount),
                "Tổng số khách phải lớn hơn 0.")]);
        }

        var query = _context.Set<Vessel>()
            .AsNoTracking()
            .Where(x => x.Status == VesselStatus.Active
                && x.SeatCount >= passengerCount);

        query = request.RentalUnit == VesselRentalUnit.Day
            ? query.Where(x => x.DailyRentalPrice.HasValue && x.DailyRentalPrice > 0)
            : query.Where(x => x.HourlyRentalPrice.HasValue && x.HourlyRentalPrice > 0);

        if (request.PreferredNumberOfDecks.HasValue)
        {
            query = query.Where(x => x.NumberOfDecks == request.PreferredNumberOfDecks.Value);
        }

        if (request.PreferredSeatSetupType.HasValue)
        {
            query = query.Where(x => x.SeatSetupType == request.PreferredSeatSetupType.Value);
        }

        var vessels = await query
            .OrderBy(x => request.RentalUnit == VesselRentalUnit.Day
                ? x.DailyRentalPrice
                : x.HourlyRentalPrice)
            .ThenBy(x => x.SeatCount)
            .ThenBy(x => x.Name)
            .ToListAsync(cancellationToken);

        var estimates = vessels
            .Select(x =>
            {
                var unitPrice = request.RentalUnit == VesselRentalUnit.Day
                    ? x.DailyRentalPrice!.Value
                    : x.HourlyRentalPrice!.Value;
                var estimatedPrice = unitPrice * request.DurationValue;

                return new RentalVesselEstimateDto(
                    x.Name,
                    x.SeatCount,
                    x.NumberOfDecks,
                    x.SeatSetupType.ToString(),
                    x.HourlyRentalPrice,
                    x.DailyRentalPrice,
                    unitPrice,
                    estimatedPrice,
                    x.Currency,
                    x.ImageUrl,
                    x.Description);
            })
            .ToList();

        return new RentalVesselEstimateListDto(
            passengerCount,
            request.AdultCount,
            request.ChildCount,
            request.RentalUnit.ToString(),
            request.DurationValue,
            estimates.Count,
            estimates.Count == 0 ? null : estimates.Min(x => x.EstimatedPrice),
            estimates.Count == 0 ? null : estimates.Max(x => x.EstimatedPrice),
            estimates);
    }
}
