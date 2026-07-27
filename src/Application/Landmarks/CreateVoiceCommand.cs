using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Application.Landmarks;

[Authorize(Roles = "Admin")]
public sealed record CreateVoiceCommand(
    string Name,
    string? VieneuVoiceId = null,
    string? RefAudioUrl = null,
    string? Region = null,
    string? Gender = null,
    string? Style = null,
    string? SampleUrl = null,
    bool IsActive = true,
    int DisplayOrder = 0) : IRequest<VoiceAdminDto>;

public sealed class CreateVoiceCommandValidator : AbstractValidator<CreateVoiceCommand>
{
    public CreateVoiceCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(150);
        RuleFor(x => x.VieneuVoiceId).MaximumLength(150);
        RuleFor(x => x.RefAudioUrl).MaximumLength(1000);
        RuleFor(x => x.Region).MaximumLength(20);
        RuleFor(x => x.Gender).MaximumLength(10);
        RuleFor(x => x.Style).MaximumLength(30);
        RuleFor(x => x.SampleUrl).MaximumLength(1000);
        // Giọng preset (vieneuVoiceId) HOẶC giọng clone (refAudioUrl) — phải có ít nhất một.
        RuleFor(x => x)
            .Must(x => !string.IsNullOrWhiteSpace(x.VieneuVoiceId) || !string.IsNullOrWhiteSpace(x.RefAudioUrl))
            .WithMessage("Phải cung cấp vieneuVoiceId (giọng preset) hoặc refAudioUrl (clip clone).");
    }
}

public sealed class CreateVoiceCommandHandler : IRequestHandler<CreateVoiceCommand, VoiceAdminDto>
{
    private readonly IApplicationDbContext _context;

    public CreateVoiceCommandHandler(IApplicationDbContext context) => _context = context;

    public async Task<VoiceAdminDto> Handle(CreateVoiceCommand request, CancellationToken cancellationToken)
    {
        var voice = new Voice
        {
            Name = request.Name.Trim(),
            VieneuVoiceId = request.VieneuVoiceId?.Trim(),
            RefAudioUrl = request.RefAudioUrl?.Trim(),
            Region = request.Region?.Trim(),
            Gender = request.Gender?.Trim(),
            Style = request.Style?.Trim(),
            SampleUrl = request.SampleUrl?.Trim(),
            IsActive = request.IsActive,
            DisplayOrder = request.DisplayOrder,
        };

        _context.Set<Voice>().Add(voice);
        await _context.SaveChangesAsync(cancellationToken);
        return VoiceAdminDto.From(voice);
    }
}
