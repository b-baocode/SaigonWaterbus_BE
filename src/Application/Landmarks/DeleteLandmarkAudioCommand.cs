using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;

namespace SaigonWaterbus.Application.Landmarks;

[Authorize(Roles = "Admin")]
public sealed record DeleteLandmarkAudioCommand(Guid LandmarkAudioId) : IRequest;

public sealed class DeleteLandmarkAudioCommandValidator : AbstractValidator<DeleteLandmarkAudioCommand>
{
    public DeleteLandmarkAudioCommandValidator() => RuleFor(x => x.LandmarkAudioId).NotEmpty();
}

public sealed class DeleteLandmarkAudioCommandHandler : IRequestHandler<DeleteLandmarkAudioCommand>
{
    private readonly IApplicationDbContext _context;

    public DeleteLandmarkAudioCommandHandler(IApplicationDbContext context) => _context = context;

    public async Task Handle(DeleteLandmarkAudioCommand request, CancellationToken cancellationToken)
    {
        var audio = await _context.Set<LandmarkAudio>()
            .SingleOrDefaultAsync(a => a.Id == request.LandmarkAudioId, cancellationToken)
            ?? throw new NotFoundException("Landmark audio not found.");

        _context.Set<LandmarkAudio>().Remove(audio);
        await _context.SaveChangesAsync(cancellationToken);
    }
}
