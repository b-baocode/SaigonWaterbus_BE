using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;

namespace SaigonWaterbus.Application.Landmarks;

[Authorize(Roles = "Admin")]
public sealed record DeleteLandmarkCommand(Guid LandmarkId) : IRequest;

public sealed class DeleteLandmarkCommandValidator : AbstractValidator<DeleteLandmarkCommand>
{
    public DeleteLandmarkCommandValidator() => RuleFor(x => x.LandmarkId).NotEmpty();
}

public sealed class DeleteLandmarkCommandHandler : IRequestHandler<DeleteLandmarkCommand>
{
    private readonly IApplicationDbContext _context;

    public DeleteLandmarkCommandHandler(IApplicationDbContext context) => _context = context;

    public async Task Handle(DeleteLandmarkCommand request, CancellationToken cancellationToken)
    {
        var landmark = await _context.Set<Landmark>()
            .SingleOrDefaultAsync(l => l.Id == request.LandmarkId, cancellationToken)
            ?? throw new NotFoundException("Landmark not found.");

        // landmark_audios.landmark_id là Cascade — xoá landmark tự xoá các bản thu của nó.
        _context.Set<Landmark>().Remove(landmark);
        await _context.SaveChangesAsync(cancellationToken);
    }
}
