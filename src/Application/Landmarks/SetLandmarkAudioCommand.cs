using FluentValidation.Results;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.Landmarks;

/// <summary>
/// Ghi nhận audio đã pre-bake cho (landmark × giọng). Upsert: có rồi thì cập nhật URL,
/// chưa có thì tạo mới — nhờ UNIQUE(landmark_id, voice_id).
/// Trả về nguyên landmark kèm toàn bộ audio (giống GET admin) để màn admin vẽ lại
/// danh sách ngay, không phải gọi thêm một lượt lấy danh sách.
/// </summary>
[Authorize(Roles = "Admin")]
public sealed record SetLandmarkAudioCommand(
    Guid LandmarkId,
    Guid VoiceId,
    string AudioUrl,
    decimal? DurationSeconds = null) : IRequest<LandmarkAdminDto>;

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

public sealed class SetLandmarkAudioCommandHandler : IRequestHandler<SetLandmarkAudioCommand, LandmarkAdminDto>
{
    private readonly IApplicationDbContext _context;

    public SetLandmarkAudioCommandHandler(IApplicationDbContext context) => _context = context;

    public async Task<LandmarkAdminDto> Handle(SetLandmarkAudioCommand request, CancellationToken cancellationToken)
    {
        var landmark = await _context.Set<Landmark>()
            .Include(l => l.Audios).ThenInclude(a => a.Voice)
            .SingleOrDefaultAsync(l => l.Id == request.LandmarkId, cancellationToken);
        if (landmark is null)
            throw new ValidationException([new ValidationFailure(nameof(request.LandmarkId), "Landmark not found.")]);

        var voice = await _context.Set<Voice>()
            .SingleOrDefaultAsync(v => v.Id == request.VoiceId, cancellationToken);
        if (voice is null)
            throw new ValidationException([new ValidationFailure(nameof(request.VoiceId), "Voice not found.")]);

        var audio = landmark.Audios.SingleOrDefault(a => a.VoiceId == request.VoiceId);

        if (audio is null)
        {
            audio = new LandmarkAudio
            {
                LandmarkId = request.LandmarkId,
                VoiceId = request.VoiceId,
                AudioUrl = request.AudioUrl.Trim(),
                DurationSeconds = request.DurationSeconds,
            };
            landmark.Audios.Add(audio);
            // Vẫn phải Add tường minh qua DbSet (giống chỗ thêm Payment vào booking): chỉ nhét vào
            // collection của landmark đang được tracking thì EF thấy Id đã có sẵn (BaseGuidEntity gán
            // Guid.NewGuid() lúc khởi tạo) nên xếp vào trạng thái Modified -> sinh UPDATE trúng 0 dòng
            // -> DbUpdateConcurrencyException -> API trả 409.
            _context.Set<LandmarkAudio>().Add(audio);
        }
        else
        {
            audio.AudioUrl = request.AudioUrl.Trim();
            audio.DurationSeconds = request.DurationSeconds;
        }

        await _context.SaveChangesAsync(cancellationToken);
        audio.Voice = voice;
        return LandmarkAdminDto.From(landmark);
    }
}
