using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;

namespace SaigonWaterbus.Application.Landmarks;

[Authorize(Roles = "Admin")]
public sealed record UpdateVoiceCommand(
    Guid VoiceId,
    string Name,
    string? VieneuVoiceId,
    string? RefAudioUrl,
    string? Region,
    string? Gender,
    string? Style,
    string? SampleUrl,
    bool IsActive,
    int DisplayOrder) : IRequest<VoiceAdminDto>;

public sealed class UpdateVoiceCommandValidator : AbstractValidator<UpdateVoiceCommand>
{
    public UpdateVoiceCommandValidator()
    {
        RuleFor(x => x.VoiceId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(150);
        RuleFor(x => x.VieneuVoiceId).MaximumLength(150);
        RuleFor(x => x.RefAudioUrl).MaximumLength(1000);
        RuleFor(x => x.Region).MaximumLength(20);
        RuleFor(x => x.Gender).MaximumLength(10);
        RuleFor(x => x.Style).MaximumLength(30);
        RuleFor(x => x.SampleUrl).MaximumLength(1000);
        RuleFor(x => x)
            .Must(x => !string.IsNullOrWhiteSpace(x.VieneuVoiceId) || !string.IsNullOrWhiteSpace(x.RefAudioUrl))
            .WithMessage("Phải cung cấp vieneuVoiceId (giọng preset) hoặc refAudioUrl (clip clone).");
    }
}

public sealed class UpdateVoiceCommandHandler : IRequestHandler<UpdateVoiceCommand, VoiceAdminDto>
{
    private readonly IApplicationDbContext _context;

    public UpdateVoiceCommandHandler(IApplicationDbContext context) => _context = context;

    public async Task<VoiceAdminDto> Handle(UpdateVoiceCommand request, CancellationToken cancellationToken)
    {
        var voice = await _context.Set<Voice>()
            .SingleOrDefaultAsync(v => v.Id == request.VoiceId, cancellationToken)
            ?? throw new NotFoundException("Voice not found.");

        voice.Name = request.Name.Trim();
        voice.VieneuVoiceId = request.VieneuVoiceId?.Trim();
        voice.RefAudioUrl = request.RefAudioUrl?.Trim();
        voice.Region = request.Region?.Trim();
        voice.Gender = request.Gender?.Trim();
        voice.Style = request.Style?.Trim();
        voice.SampleUrl = request.SampleUrl?.Trim();
        voice.IsActive = request.IsActive;
        voice.DisplayOrder = request.DisplayOrder;

        await _context.SaveChangesAsync(cancellationToken);
        return VoiceAdminDto.From(voice);
    }
}
