using FluentValidation.Results;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.Landmarks;

[Authorize(Roles = "Admin")]
public sealed record DeleteVoiceCommand(Guid VoiceId) : IRequest;

public sealed class DeleteVoiceCommandValidator : AbstractValidator<DeleteVoiceCommand>
{
    public DeleteVoiceCommandValidator() => RuleFor(x => x.VoiceId).NotEmpty();
}

public sealed class DeleteVoiceCommandHandler : IRequestHandler<DeleteVoiceCommand>
{
    private readonly IApplicationDbContext _context;

    public DeleteVoiceCommandHandler(IApplicationDbContext context) => _context = context;

    public async Task Handle(DeleteVoiceCommand request, CancellationToken cancellationToken)
    {
        var voice = await _context.Set<Voice>()
            .SingleOrDefaultAsync(v => v.Id == request.VoiceId, cancellationToken)
            ?? throw new NotFoundException("Voice not found.");

        // FK landmark_audios.voice_id là Restrict — chặn xoá giọng còn audio để tránh mồ côi.
        var hasAudio = await _context.Set<LandmarkAudio>()
            .AnyAsync(a => a.VoiceId == voice.Id, cancellationToken);
        if (hasAudio)
        {
            throw new ValidationException([new ValidationFailure(
                nameof(request.VoiceId),
                "Không xoá được giọng vì còn bản thu landmark. Hãy đặt IsActive=false hoặc xoá audio trước.")]);
        }

        _context.Set<Voice>().Remove(voice);
        await _context.SaveChangesAsync(cancellationToken);
    }
}
