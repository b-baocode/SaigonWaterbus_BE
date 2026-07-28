using FluentValidation.Results;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.Landmarks;

/// <summary>
/// Ghi nhận audio đã pre-bake cho (landmark × giọng). Upsert: có rồi thì cập nhật URL,
/// chưa có thì tạo mới — nhờ UNIQUE(landmark_id, voice_id).
/// </summary>
[Authorize(Roles = "Admin")]
public sealed record SetLandmarkAudioCommand(
    Guid LandmarkId,
    Guid VoiceId,
    string AudioUrl,
    decimal? DurationSeconds = null) : IRequest<LandmarkAudioDto>;

public sealed class SetLandmarkAudioCommandValidator : AbstractValidator<SetLandmarkAudioCommand>
{
    public SetLandmarkAudioCommandValidator()
    {
        RuleFor(x => x.LandmarkId).NotEmpty();
        RuleFor(x => x.VoiceId).NotEmpty();
        RuleFor(x => x.AudioUrl).NotEmpty().MaximumLength(1000)
            .Must(u => Uri.TryCreate(u, UriKind.Absolute, out _))
            .WithMessage("AudioUrl phải là URL tuyệt đối.");
        RuleFor(x => x.DurationSeconds).GreaterThan(0).When(x => x.DurationSeconds.HasValue);
    }
}

public sealed class SetLandmarkAudioCommandHandler : IRequestHandler<SetLandmarkAudioCommand, LandmarkAudioDto>
{
    private readonly IApplicationDbContext _context;

    public SetLandmarkAudioCommandHandler(IApplicationDbContext context) => _context = context;

    public async Task<LandmarkAudioDto> Handle(SetLandmarkAudioCommand request, CancellationToken cancellationToken)
    {
        var landmarkExists = await _context.Set<Landmark>()
            .AnyAsync(l => l.Id == request.LandmarkId, cancellationToken);
        if (!landmarkExists)
            throw new ValidationException([new ValidationFailure(nameof(request.LandmarkId), "Landmark not found.")]);

        var voice = await _context.Set<Voice>()
            .SingleOrDefaultAsync(v => v.Id == request.VoiceId, cancellationToken);
        if (voice is null)
            throw new ValidationException([new ValidationFailure(nameof(request.VoiceId), "Voice not found.")]);

        var audio = await _context.Set<LandmarkAudio>()
            .SingleOrDefaultAsync(a => a.LandmarkId == request.LandmarkId && a.VoiceId == request.VoiceId, cancellationToken);

        if (audio is null)
        {
            audio = new LandmarkAudio
            {
                LandmarkId = request.LandmarkId,
                VoiceId = request.VoiceId,
                AudioUrl = request.AudioUrl.Trim(),
                DurationSeconds = request.DurationSeconds,
            };
            _context.Set<LandmarkAudio>().Add(audio);
        }
        else
        {
            audio.AudioUrl = request.AudioUrl.Trim();
            audio.DurationSeconds = request.DurationSeconds;
        }

        await _context.SaveChangesAsync(cancellationToken);
        audio.Voice = voice;
        return LandmarkAudioDto.From(audio);
    }
}
