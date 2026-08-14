using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;

namespace SaigonWaterbus.Application.Knowledge;

/// <summary>
/// Bật/tắt hiển thị cho trợ lý mà không phải gửi lại toàn bộ nội dung. Chuyển về Draft là cách
/// rút một mẩu kiến thức sai ra khỏi chatbot ngay, không cần xoá.
/// </summary>
[Authorize(Roles = "Admin")]
public sealed record UpdateKnowledgeEntryStatusCommand(Guid KnowledgeEntryId, string Status)
    : IRequest<KnowledgeEntryDto>;

public sealed class UpdateKnowledgeEntryStatusCommandValidator
    : AbstractValidator<UpdateKnowledgeEntryStatusCommand>
{
    public UpdateKnowledgeEntryStatusCommandValidator()
    {
        RuleFor(x => x.KnowledgeEntryId).NotEmpty();
        RuleFor(x => x.Status)
            .NotEmpty()
            .Must(KnowledgeEntry.IsValidStatus)
            .WithMessage($"Status hop le: {string.Join(" | ", KnowledgeEntry.AllStatuses)}.");
    }
}

public sealed class UpdateKnowledgeEntryStatusCommandHandler
    : IRequestHandler<UpdateKnowledgeEntryStatusCommand, KnowledgeEntryDto>
{
    private readonly IApplicationDbContext _context;
    private readonly TimeProvider _timeProvider;

    public UpdateKnowledgeEntryStatusCommandHandler(IApplicationDbContext context, TimeProvider timeProvider)
    {
        _context = context;
        _timeProvider = timeProvider;
    }

    public async Task<KnowledgeEntryDto> Handle(
        UpdateKnowledgeEntryStatusCommand request,
        CancellationToken cancellationToken)
    {
        var entry = await _context.Set<KnowledgeEntry>()
            .SingleOrDefaultAsync(x => x.Id == request.KnowledgeEntryId, cancellationToken)
            ?? throw new NotFoundException("Knowledge entry not found.");

        entry.Status = KnowledgeEntrySupport.ResolveStatus(request.Status);
        entry.UpdatedAt = _timeProvider.GetUtcNow();

        await _context.SaveChangesAsync(cancellationToken);
        return KnowledgeEntrySupport.ToDto(entry);
    }
}
