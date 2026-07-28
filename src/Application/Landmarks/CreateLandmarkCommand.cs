using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Application.Landmarks;

[Authorize(Roles = "Admin")]
public sealed record CreateLandmarkCommand(
    string LandmarkName,
    decimal Latitude,
    decimal Longitude,
    string? Description = null,
    int DisplayOrder = 0,
    int TriggerRadiusMeters = 300,
    bool IsActive = true) : IRequest<LandmarkAdminDto>;

public sealed class CreateLandmarkCommandValidator : AbstractValidator<CreateLandmarkCommand>
{
    public CreateLandmarkCommandValidator()
    {
        RuleFor(x => x.LandmarkName).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Description).MaximumLength(2000);
        RuleFor(x => x.Latitude).InclusiveBetween(-90m, 90m);
        RuleFor(x => x.Longitude).InclusiveBetween(-180m, 180m);
        RuleFor(x => x.TriggerRadiusMeters).GreaterThan(0).LessThanOrEqualTo(5000);
    }
}

public sealed class CreateLandmarkCommandHandler : IRequestHandler<CreateLandmarkCommand, LandmarkAdminDto>
{
    private readonly IApplicationDbContext _context;

    public CreateLandmarkCommandHandler(IApplicationDbContext context) => _context = context;

    public async Task<LandmarkAdminDto> Handle(CreateLandmarkCommand request, CancellationToken cancellationToken)
    {
        var landmark = new Landmark
        {
            LandmarkName = request.LandmarkName.Trim(),
            Description = request.Description?.Trim(),
            Latitude = request.Latitude,
            Longitude = request.Longitude,
            DisplayOrder = request.DisplayOrder,
            TriggerRadiusMeters = request.TriggerRadiusMeters,
            IsActive = request.IsActive,
        };

        _context.Set<Landmark>().Add(landmark);
        await _context.SaveChangesAsync(cancellationToken);
        return LandmarkAdminDto.From(landmark);
    }
}
