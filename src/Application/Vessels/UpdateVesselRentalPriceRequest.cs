using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Application.Vessels;

public sealed record UpdateVesselRentalPriceRequest(
    Guid VesselId,
    decimal UnitPrice,
    string? Currency = null,
    string? Note = null,
    VesselRentalUnit RentalUnit = VesselRentalUnit.Day);

public sealed class UpdateVesselRentalPriceRequestValidator : AbstractValidator<UpdateVesselRentalPriceRequest>
{
    public UpdateVesselRentalPriceRequestValidator()
    {
        RuleFor(x => x.VesselId)
            .NotEmpty()
            .WithMessage("VesselId không hợp lệ.");

        RuleFor(x => x.UnitPrice)
            .GreaterThan(0)
            .WithMessage("Giá thuê tàu phải lớn hơn 0.")
            .LessThanOrEqualTo(9999999999.99m)
            .WithMessage("Giá thuê tàu không hợp lệ.");

        RuleFor(x => x.RentalUnit)
            .IsInEnum()
            .WithMessage("Đơn vị thuê tàu chỉ được là Hour hoặc Day.");

        RuleFor(x => x.Currency)
            .Must(VesselSupport.IsValidCurrencyCode)
            .WithMessage("Currency phải là mã ISO 4217 gồm 3 chữ cái, ví dụ VND.")
            .When(x => x.Currency is not null);

        RuleFor(x => x.Note)
            .MaximumLength(500)
            .WithMessage("Ghi chú giá thuê tàu không được vượt quá 500 ký tự.");
    }
}

public sealed class UpdateVesselRentalPriceRequestUseCase
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;

    public UpdateVesselRentalPriceRequestUseCase(
        IApplicationDbContext context,
        IUserContext userContext)
    {
        _context = context;
        _userContext = userContext;
    }

    public async Task<VesselDto> ExecuteAsync(
        UpdateVesselRentalPriceRequest request,
        CancellationToken cancellationToken)
    {
        await VesselSupport.EnsureCurrentUserCanManageVesselsAsync(_context, _userContext, cancellationToken);

        var vessel = await _context.Vessels
            .Include(x => x.RentalPrices)
            .SingleOrDefaultAsync(x => x.Id == request.VesselId, cancellationToken)
            ?? throw new SaigonWaterbus.Application.Common.Exceptions.NotFoundException("Không tìm thấy tàu.");

        var rentalPrice = vessel.RentalPrices.SingleOrDefault(x => x.RentalUnit == request.RentalUnit);
        if (rentalPrice is null)
        {
            rentalPrice = new VesselRentalPrice
            {
                VesselId = vessel.Id,
                RentalUnit = request.RentalUnit
            };
            vessel.RentalPrices.Add(rentalPrice);
            _context.VesselRentalPrices.Add(rentalPrice);
        }

        rentalPrice.UnitPrice = request.UnitPrice;
        rentalPrice.Currency = VesselSupport.NormalizeCurrency(request.Currency);
        rentalPrice.Note = VesselSupport.NormalizeOptionalNote(request.Note);

        await _context.SaveChangesAsync(cancellationToken);

        return VesselSupport.CreateDto(vessel);
    }
}
