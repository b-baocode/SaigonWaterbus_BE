using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Application.Fares;

public sealed record FarePolicyDto(
    Guid? FarePolicyId,
    decimal BaseFare,
    decimal PricePerKm,
    decimal RoundingStep,
    decimal? MinFare,
    string Currency);

/// <summary>Giá trị mặc định khi DB chưa có policy active (khớp seed trong migration).</summary>
public static class FarePolicyDefaults
{
    public const decimal BaseFare = 5000m;
    public const decimal PricePerKm = 1500m;
    public const decimal RoundingStep = 1000m;

    public static FarePolicyDto Dto { get; } = new(null, BaseFare, PricePerKm, RoundingStep, null, "VND");
}

public sealed record GetFarePolicyQuery : IRequest<FarePolicyDto>;

public sealed class GetFarePolicyQueryHandler : IRequestHandler<GetFarePolicyQuery, FarePolicyDto>
{
    private readonly IApplicationDbContext _context;

    public GetFarePolicyQueryHandler(IApplicationDbContext context) => _context = context;

    public Task<FarePolicyDto> Handle(GetFarePolicyQuery request, CancellationToken cancellationToken) =>
        DistanceFareSupport.GetActivePolicyAsync(_context, cancellationToken);
}

[Authorize(Roles = "Admin,Manager")]
public sealed record UpdateFarePolicyCommand(
    decimal BaseFare,
    decimal PricePerKm,
    decimal RoundingStep = 1000m,
    decimal? MinFare = null) : IRequest<FarePolicyDto>;

public sealed class UpdateFarePolicyCommandValidator : AbstractValidator<UpdateFarePolicyCommand>
{
    public UpdateFarePolicyCommandValidator()
    {
        RuleFor(x => x.BaseFare)
            .GreaterThanOrEqualTo(0).WithMessage("Phí cơ bản không được âm.")
            .LessThanOrEqualTo(100_000_000).WithMessage("Phí cơ bản tối đa 100.000.000 VND.")
            .Must(x => decimal.Truncate(x) == x).WithMessage("Phí cơ bản phải là số nguyên VND.");
        RuleFor(x => x.PricePerKm)
            .GreaterThan(0).WithMessage("Đơn giá mỗi km phải lớn hơn 0.")
            .LessThanOrEqualTo(1_000_000).WithMessage("Đơn giá mỗi km tối đa 1.000.000 VND.")
            .Must(x => decimal.Truncate(x) == x).WithMessage("Đơn giá mỗi km phải là số nguyên VND.");
        RuleFor(x => x.RoundingStep)
            .Must(x => x is 1m or 100m or 500m or 1000m)
            .WithMessage("Bước làm tròn chỉ nhận 1, 100, 500 hoặc 1000 VND.");
        RuleFor(x => x.MinFare)
            .Must(x => x is null || (x > 0 && x <= 100_000_000 && decimal.Truncate(x.Value) == x))
            .WithMessage("Giá sàn phải là số nguyên VND lớn hơn 0.");
    }
}

public sealed class UpdateFarePolicyCommandHandler : IRequestHandler<UpdateFarePolicyCommand, FarePolicyDto>
{
    private readonly IApplicationDbContext _context;

    public UpdateFarePolicyCommandHandler(IApplicationDbContext context) => _context = context;

    public async Task<FarePolicyDto> Handle(UpdateFarePolicyCommand request, CancellationToken cancellationToken)
    {
        var policy = await _context.Set<FarePolicy>()
            .Where(x => x.IsActive)
            .OrderByDescending(x => x.Created)
            .FirstOrDefaultAsync(cancellationToken);

        if (policy is null)
        {
            policy = new FarePolicy();
            _context.Set<FarePolicy>().Add(policy);
        }

        policy.BaseFare = request.BaseFare;
        policy.PricePerKm = request.PricePerKm;
        policy.RoundingStep = request.RoundingStep;
        policy.MinFare = request.MinFare;
        policy.IsActive = true;

        await _context.SaveChangesAsync(cancellationToken);

        return new FarePolicyDto(policy.Id, policy.BaseFare, policy.PricePerKm,
            policy.RoundingStep, policy.MinFare, policy.Currency);
    }
}
