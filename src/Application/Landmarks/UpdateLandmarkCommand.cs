using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;

namespace SaigonWaterbus.Application.Landmarks;

[Authorize(Roles = "Admin")]
public sealed record UpdateLandmarkCommand(
    Guid LandmarkId,
    string LandmarkName,
    decimal Latitude,
    decimal Longitude,
    string? Description,
    int DisplayOrder,
    int TriggerRadiusMeters,
    bool IsActive) : IRequest<LandmarkAdminDto>;

public sealed class UpdateLandmarkCommandValidator : AbstractValidator<UpdateLandmarkCommand>
{
    public UpdateLandmarkCommandValidator()
    {
        RuleFor(x => x.LandmarkId).NotEmpty();
        RuleFor(x => x.LandmarkName).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Description).MaximumLength(2000);
        RuleFor(x => x.Latitude).InclusiveBetween(-90m, 90m);
        RuleFor(x => x.Longitude).InclusiveBetween(-180m, 180m);
        RuleFor(x => x.TriggerRadiusMeters).GreaterThan(0).LessThanOrEqualTo(5000);
    }
}

public sealed class UpdateLandmarkCommandHandler : IRequestHandler<UpdateLandmarkCommand, LandmarkAdminDto>
{
    private readonly IApplicationDbContext _context;

    public UpdateLandmarkCommandHandler(IApplicationDbContext context) => _context = context;

    public async Task<LandmarkAdminDto> Handle(UpdateLandmarkCommand request, CancellationToken cancellationToken)
    {
        var landmark = await _context.Set<Landmark>()
            .Include(l => l.Audios).ThenInclude(a => a.Voice)
            .SingleOrDefaultAsync(l => l.Id == request.LandmarkId, cancellationToken)
            ?? throw new NotFoundException("Landmark not found.");

        landmark.LandmarkName = request.LandmarkName.Trim();
        landmark.Description = request.Description?.Trim();
        landmark.Latitude = request.Latitude;
        landmark.Longitude = request.Longitude;
        landmark.DisplayOrder = request.DisplayOrder;
        landmark.TriggerRadiusMeters = request.TriggerRadiusMeters;
        landmark.IsActive = request.IsActive;

        await _context.SaveChangesAsync(cancellationToken);
        return LandmarkAdminDto.From(landmark);
    }
}
