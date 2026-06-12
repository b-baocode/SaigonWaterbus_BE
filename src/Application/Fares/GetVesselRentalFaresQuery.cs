using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Application.Fares;

public sealed record GetVesselRentalFaresQuery(
    Guid? ServiceId = null,
    string? Search = null) : IRequest<IReadOnlyList<VesselRentalFareDto>>;

public sealed class GetVesselRentalFaresQueryValidator : AbstractValidator<GetVesselRentalFaresQuery>
{
    public GetVesselRentalFaresQueryValidator()
    {
        RuleFor(x => x.ServiceId)
            .Must(serviceId => serviceId != Guid.Empty)
            .WithMessage("ServiceId không hợp lệ.")
            .When(x => x.ServiceId.HasValue);

        RuleFor(x => x.Search)
            .MaximumLength(100)
            .WithMessage("Từ khóa tìm kiếm không được vượt quá 100 ký tự.");
    }
}

public sealed class GetVesselRentalFaresQueryHandler
    : IRequestHandler<GetVesselRentalFaresQuery, IReadOnlyList<VesselRentalFareDto>>
{
    private readonly IApplicationDbContext _context;

    public GetVesselRentalFaresQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<VesselRentalFareDto>> Handle(
        GetVesselRentalFaresQuery request,
        CancellationToken cancellationToken)
    {
        var query = _context.Vessels
            .AsNoTracking()
            .Include(x => x.WaterbusService)
            .Include(x => x.RentalPrices)
            .Where(x =>
                x.Status == VesselStatus.Active
                && x.SeatsConfigured
                && x.WaterbusService.IsActive
                && x.WaterbusService.BookingMode == BookingMode.VesselRental
                && x.RentalPrices.Any(p => p.RentalUnit == VesselRentalUnit.Day))
            .AsQueryable();

        if (request.ServiceId.HasValue)
        {
            query = query.Where(x => x.WaterbusServiceId == request.ServiceId.Value);
        }

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var keyword = request.Search.Trim().ToUpperInvariant();
            query = query.Where(x =>
                x.Code.Contains(keyword)
                || x.Name.ToUpper().Contains(keyword)
                || x.WaterbusService.Code.Contains(keyword)
                || x.WaterbusService.Name.ToUpper().Contains(keyword));
        }

        var vessels = await query
            .OrderBy(x => x.Code)
            .ThenBy(x => x.Id)
            .ToListAsync(cancellationToken);

        return vessels
            .Select(v =>
            {
                var rentalPrice = v.RentalPrices.Single(p => p.RentalUnit == VesselRentalUnit.Day);
                var description = !string.IsNullOrWhiteSpace(v.Description)
                    ? v.Description
                    : v.WaterbusService.Description;

                return new VesselRentalFareDto(
                    v.Id,
                    v.Code,
                    v.Name,
                    v.SeatCount,
                    v.PassengerCapacity,
                    v.NumberOfDecks,
                    v.ImageUrl ?? string.Empty,
                    description,
                    rentalPrice.RentalUnit,
                    rentalPrice.UnitPrice,
                    rentalPrice.Currency,
                    rentalPrice.Note);
            })
            .ToArray();
    }
}
